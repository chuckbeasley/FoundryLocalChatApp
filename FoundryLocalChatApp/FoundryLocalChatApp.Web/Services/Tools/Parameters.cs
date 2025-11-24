public record Parameters
{
    public string Type { get; set; }
    public IReadOnlyDictionary<string, Properties>[] Properties { get; set; }
    public string[] Required { get; set; }
}
