namespace GothicTranslator;

public sealed class Settings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string OutputTokens { get; set; } = string.Empty;
    public string TokenFactor { get; set; } = string.Empty;
    public string ThinkingTokens { get; set; } = string.Empty;
    public string InputFile { get; set; } = string.Empty;
    public string DictionaryFile { get; set; } = string.Empty;
    public bool ShowDebug { get; set; } = false;
    public string SourceLanguage { get; set; } = string.Empty;
    public string SourceHeader { get; set; } = string.Empty;
    public string TargetLanguage { get; set; } = string.Empty;
    public string TargetHeader { get; set; } = string.Empty;
}
