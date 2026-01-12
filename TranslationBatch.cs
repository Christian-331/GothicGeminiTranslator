using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GothicTranslator;

public sealed class TranslationBatch
{
    [JsonPropertyName("d")]
    public List<TranslatedRecord>? D { get; set; }
}
