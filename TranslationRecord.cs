namespace GothicTranslator;

public sealed class TranslationRecord
{
    // ReSharper disable InconsistentNaming
    public int? NR { get; set; }
    public int? FILENR { get; set; }
    public int? ID { get; set; }
    public string? SYMBOL { get; set; }
    public string? USE { get; set; }
    public string? TRACE	 { get; set; }
    public string? OriginalText { get; set; }
    public string? TranslatedText { get; set; }
    // ReSharper restore InconsistentNaming
}
