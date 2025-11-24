using Microsoft.AI.Foundry.Local.Detail;
using System.Reflection;
using System.Threading.Channels;

namespace FoundryLocalChatApp.Web.Services
{
    // Encapsulates minimal reflection/dynamic operations against the internal Foundry interop.
    internal static class FoundryInteropHelper
    {
        public static object? GetCoreInterop(object foundryClientOrManager)
        {
            if (foundryClientOrManager == null)
            {
                return null;
            }

            try
            {
                var t = foundryClientOrManager.GetType();
                // If this is an OpenAIChatClient instance, try to get _coreInterop field
                var field = t.GetField("_coreInterop", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    return field.GetValue(foundryClientOrManager);
                }

                // Try CoreInterop property
                var prop = t.GetProperty("CoreInterop", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (prop != null)
                {
                    return prop.GetValue(foundryClientOrManager);
                }
            }
            catch { }

            return null;
        }

        public static object? GetCoreInteropFromManagerSingleton()
        {
            try
            {
                var flType = Type.GetType("Microsoft.AI.Foundry.Local.FoundryLocalManager, Microsoft.AI.Foundry.Local");
                if (flType == null)
                {
                    return null;
                }

                var instProp = flType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                var instance = instProp?.GetValue(null);
                if (instance == null)
                {
                    return null;
                }

                var coreProp = instance.GetType().GetProperty("CoreInterop", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return coreProp?.GetValue(instance);
            }
            catch { }

            return null;
        }

        public static string? GetModelIdFromOpenAIChatClient(object openAiChatClient)
        {
            if (openAiChatClient == null)
            {
                return null;
            }

            try
            {
                var t = openAiChatClient.GetType();
                var field = t.GetField("_modelId", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    return field.GetValue(openAiChatClient) as string;
                }

                var prop = t.GetProperty("ModelId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    return prop.GetValue(openAiChatClient) as string;
                }
            }
            catch { }

            return null;
        }

        // Create a CoreInteropRequest instance via reflection from the core interop's assembly.
        public static CoreInteropRequest CreateCoreInteropRequest(object coreInterop, string json)
        {
            if (coreInterop == null)
            {
                return null!;
            }

            try
            {
                var coreType = coreInterop.GetType();
                var asm = coreType.Assembly;
                var coreReqType = asm.GetType("Microsoft.AI.Foundry.Local.Detail.CoreInteropRequest")
                                 ?? asm.GetType("Microsoft.AI.Foundry.Local.CoreInteropRequest");
                if (coreReqType == null)
                {
                    return null;
                }

                var instance = Activator.CreateInstance(coreReqType);
                var paramsProp = coreReqType.GetProperty("Params", BindingFlags.Public | BindingFlags.Instance);
                if (paramsProp != null)
                {
                    paramsProp.SetValue(instance, new Dictionary<string, string> { { "OpenAICreateRequest", json } });
                    return (CoreInteropRequest)instance!;
                }

                var field = coreReqType.GetField("Params", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(instance, new Dictionary<string, string> { { "OpenAICreateRequest", json } });
                    return (CoreInteropRequest)instance!;
                }

                return null!;
            }
            catch
            {
                return null!;
            }
        }

        public static async Task<string?> ExecuteCommandAsync(object coreInterop, object coreReqObj, string commandName, CancellationToken ct)
        {
            if (coreInterop == null)
            {
                return null;
            }

            try
            {
                dynamic dyn = coreInterop;
                var task = (Task)dyn.ExecuteCommandAsync(commandName, coreReqObj, ct);
                await task.ConfigureAwait(false);

                var resultProp = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
                var resp = resultProp?.GetValue(task);
                if (resp == null)
                {
                    return null;
                }

                var dataField = resp.GetType().GetField("Data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (dataField != null)
                {
                    return dataField.GetValue(resp) as string;
                }

                var dataProp = resp.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (dataProp != null)
                {
                    return dataProp.GetValue(resp) as string;
                }

                return resp.ToString();
            }
            catch
            {
                return null;
            }
        }

        // ExecuteCommandWithCallbackAsync streaming helper: returns an async enumerable of callback JSON strings.
        public static async IAsyncEnumerable<string> ExecuteCommandWithCallbackAsStream(object coreInterop, object coreReqObj, string commandName, CancellationToken ct)
        {
            var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
                AllowSynchronousContinuations = true
            });

            // fire-and-forget the execution; callbacks will write to the channel
            _ = Task.Run(async () =>
            {
                try
                {
                    // create the callback delegate (public Action<string> for our shim)
                    Action<string> callback = (string callbackData) =>
                    {
                        try
                        {
                            channel.Writer.TryWrite(callbackData);
                        }
                        catch
                        {
                        }
                    };

                    var coreType = coreInterop.GetType();
                    // find the ExecuteCommandWithCallbackAsync method (internal or public)
                    var method = coreType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                         .FirstOrDefault(m => string.Equals(m.Name, "ExecuteCommandWithCallbackAsync", StringComparison.Ordinal)
                                                              && m.GetParameters().Length >= 3);
                    if (method == null)
                    {
                        try { channel.Writer.TryComplete(); } catch { }
                        return;
                    }

                    // Build arguments array matching parameter count; many signatures include cancellation token as last param.
                    var parameters = method.GetParameters();
                    var args = new object[parameters.Length];

                    if (parameters.Length >= 1)
                    {
                        args[0] = commandName;
                    }

                    if (parameters.Length >= 2)
                    {
                        args[1] = coreReqObj!;
                    }

                    // The Foundry callback delegate type is internal. Create a delegate instance of that internal type
                    // that wraps our public Action<string> callback.
                    if (parameters.Length >= 3)
                    {
                        // Try to locate the internal callback delegate type in the same assembly as the core interop.
                        var asm = coreType.Assembly;
                        var callbackType = asm.GetType("Microsoft.AI.Foundry.Local.Detail.ICoreInterop+CallbackFn")
                                           ?? asm.GetType("Microsoft.AI.Foundry.Local.ICoreInterop+CallbackFn")
                                           ?? asm.GetType("Microsoft.AI.Foundry.Local.Detail.CoreInterop+CallbackFn")
                                           ?? asm.GetType("Microsoft.AI.Foundry.Local.CoreInterop+CallbackFn");

                        object? callbackDelegateObj = null;
                        if (callbackType != null)
                        {
                            try
                            {
                                // Use the MethodInfo and target from our Action<string> to create a delegate of the internal type.
                                var methodInfo = callback.Method;
                                var target = callback.Target;
                                callbackDelegateObj = Delegate.CreateDelegate(callbackType, target, methodInfo);
                            }
                            catch
                            {
                                // fallback to null; we'll attempt to pass the Action and let Invoke fail if incompatible.
                                callbackDelegateObj = null;
                            }
                        }

                        // If we successfully created the internal-delegate instance use it; otherwise fall back to the public Action.
                        args[2] = callbackDelegateObj ?? callback;
                    }

                    if (parameters.Length >= 4)
                    {
                        args[3] = ct;
                    }

                    var execTaskObj = method.Invoke(coreInterop, args);
                    if (execTaskObj is Task execTask)
                    {
                        await execTask.ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        channel.Writer.TryComplete();
                    }
                    catch
                    {
                    }
                    return;
                }

                try
                {
                    channel.Writer.TryComplete();
                }
                catch
                {
                }
            }, ct);

            await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }
}
