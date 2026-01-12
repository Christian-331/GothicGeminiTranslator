using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using Google.GenAI;
using Google.GenAI.Types;
using Environment = System.Environment;
using File = System.IO.File;
using Type = Google.GenAI.Types.Type;

namespace GothicTranslator;

public static class ApiHandler
{
    /// <summary>
    /// How many milliseconds to wait between API calls to prevent rate limiting.
    /// </summary>
    private const int RateLimitDelay = 1000;

    private static MainWindow? _window;

    public static int Count { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        Delimiter = "\t",
        MissingFieldFound = null,
        HeaderValidated = null,
        Encoding = Encoding.UTF8,
        Mode = CsvMode.NoEscape,
        PrepareHeaderForMatch = args => args.Header.ToUpper(),
    };

    private static readonly List<SafetySetting> SafetySettings =
    [
        new()
        {
            Category = HarmCategory.HARM_CATEGORY_HARASSMENT,
            Threshold = HarmBlockThreshold.BLOCK_NONE
        },
        new()
        {
            Category = HarmCategory.HARM_CATEGORY_HATE_SPEECH,
            Threshold = HarmBlockThreshold.BLOCK_NONE
        },
        new()
        {
            Category = HarmCategory.HARM_CATEGORY_SEXUALLY_EXPLICIT,
            Threshold = HarmBlockThreshold.BLOCK_NONE
        },
        new()
        {
            Category = HarmCategory.HARM_CATEGORY_DANGEROUS_CONTENT,
            Threshold = HarmBlockThreshold.BLOCK_NONE
        },
        new()
        {
            Category = HarmCategory.HARM_CATEGORY_CIVIC_INTEGRITY,
            Threshold = HarmBlockThreshold.BLOCK_NONE
        },
    ];

    public static string ApiKey { get; set; } = string.Empty;

    public static string ModelName { get; set; } = string.Empty;

    public static string InputFile { get; set; } = string.Empty;

    public static string DictionaryFile { get; set; } = string.Empty;

    public static string SourceLanguage { get; set; } = string.Empty;

    public static string SourceHeader { get; set; } = string.Empty;

    public static string TargetLanguage { get; set; } = string.Empty;

    public static string TargetHeader { get; set; } = string.Empty;

    public static int OutputTokens { get; set; }

    public static double TokenFactor { get; set; }

    public static int ThinkingTokens { get; set; }

    public static bool Aborting { get; set; }

    public static bool Stopping { get; set; }

    public static void SetMainWindow(MainWindow mainWindow)
    {
        _window = mainWindow;
    }

    private static string SerializePrompt(List<TranslationRecord> list, JsonSerializerOptions requestOptions)
    {
        var batchForPrompt = new
        {
            d = list,
        };
        string jsonBatch = JsonSerializer.Serialize(batchForPrompt, requestOptions);

        string prePrompt =
            $$"""
              You are a professional localizer for the medieval fantasy RPG series "Gothic".
              Translate the text contained in the {{SourceHeader}} field from {{SourceLanguage}} to {{TargetLanguage}} and write the result into the {{TargetHeader}} field.
              If the {{TargetHeader}} field is already {{TargetLanguage}}, leave it unaltered!
              Style Guidelines: Keep the tone rough, conversational and grounded in a medieval setting. Avoid modern slang or polite flowery language.
              Output Format: Return a minified JSON array containing objects with strictly two fields: NR and {{TargetHeader}}. Exclude all other fields.
              Output Format Example:
              [{"NR":1,"{{TargetHeader}}":"Translation here"},{"NR":2,"{{TargetHeader}}":"Another translation"}]
              """;

        string dictionaryText = string.Empty;
        if (!string.IsNullOrEmpty(DictionaryFile))
        {
            dictionaryText = Environment.NewLine
                + "Use the following dictionary for common terms:"
                + Environment.NewLine
                + File.ReadAllText(DictionaryFile);
        }

        const string prePromptEnd =
            """

            JSON Input:

            """;

        return prePrompt + dictionaryText + prePromptEnd + jsonBatch;
    }

    private static string SerializeOutput(List<TranslatedRecord> list)
    {
        var batchForPrompt = new
        {
            d = list,
        };
        return JsonSerializer.Serialize(batchForPrompt, Options);
    }

    public static async Task RunTranslationProcess()
    {
        Count++;
        int currentCount = Count;

        if (_window is null)
            return;

        string directory = Path.GetDirectoryName(InputFile) ?? "";
        string fileNameNoExt = Path.GetFileNameWithoutExtension(InputFile);
        string backupFile = Path.Combine(
            directory,
            fileNameNoExt + $"_backup_{DateTime.Now:yyyy-MM-dd-hh-mm-ss}.csv");

        Log($"Writing backup file \"{backupFile}\"...");
        File.Copy(InputFile, backupFile);
        Log($"\"{backupFile}\" saved!");

        List<TranslationRecord> allRecords;

        using (StreamReader reader = new(InputFile))
        using (CsvReader csv = new(reader, CsvConfig))
        {
            csv.Context.RegisterClassMap(new DynamicMap(SourceHeader, TargetHeader));
            allRecords = csv
                .GetRecords<TranslationRecord>()
                .ToList();
        }

        allRecords.ForEach(r => r.OriginalText ??= string.Empty);
        allRecords.ForEach(r => r.TranslatedText ??= string.Empty);

        List<TranslationRecord> missingRows = allRecords
            .Where(r => r.OriginalText != string.Empty
                && r.NR.HasValue
                && (r.TranslatedText == string.Empty
                    || r.TranslatedText == r.OriginalText
                    || Check(r.OriginalText!, r.TranslatedText!)))
            .ToList();
        int totalMissing = missingRows.Count;

        // ReSharper disable once MoveLocalFunctionAfterJumpStatement
        static bool Check(string s1, string s2)
        {
            if (s1.Length != s2.Length) return false;

            // ReSharper disable once LoopCanBeConvertedToQuery
            for (int i = 0; i < s1.Length; i++)
            {
                if ((s1[i] == ' ' || s2[i] == ' ')
                    && s1[i] != s2[i])
                {
                    return false;
                }
            }

            return true;
        }

        Log($"Total rows: {allRecords.Count}");
        Log($"Untranslated rows: {totalMissing} ({ (double)totalMissing / allRecords.Count * 100:F1}%)");

        if (totalMissing == 0)
        {
            Log("Nothing to translate!");
            return;
        }

        bool hasChanges = false;

        JsonSerializerOptions requestOptions = GetRequestOptions();
        JsonSerializerOptions geminiOptions = GetGeminiOptions();

        for (int i = 0; i < totalMissing; i++)
        {
            if (Aborting) return;

            if (Stopping)
            {
                Logger.Log("Stopping due to user request...");
                break;
            }

            List<TranslationRecord> list = [];
            while (i < totalMissing)
            {
                TranslationRecord currentRecord = missingRows[i];
                List<TranslatedRecord> futureList = list
                    .Select(Get)
                    .ToList();
                futureList.Add(Get(currentRecord));
                string jsonPayload = SerializeOutput(futureList);

                int estimatedOutputTokens = (int)(jsonPayload.Length * TokenFactor);
                if (estimatedOutputTokens > OutputTokens - ThinkingTokens) break;

                list.Add(currentRecord);
                i++;

                continue;

                static TranslatedRecord Get(TranslationRecord record) => new()
                {
                    NR = record.NR,
                    TL = record.OriginalText
                };
            }

            int rowCount = list.Count;

            if (rowCount == 0)
            {
                Log($"Row with NR {missingRows[i].NR} is too large and gets skipped!");
                continue;
            }

            Log($"Sending {rowCount} {(rowCount == 1 ? $"row" : $"rows")} for translation...");

            string prompt = SerializePrompt(list, requestOptions);

            const int promptDisplayLength = 2000;
            string logPrompt = prompt[..Math.Min(prompt.Length, promptDisplayLength)];
            if (logPrompt.Length > promptDisplayLength)
                logPrompt += "...";
            Logger.Debug($"Sent prompt (first {promptDisplayLength} characters):{Environment.NewLine}{logPrompt}", currentCount);

            try
            {
                List<TranslatedRecord> responseData = await CallGeminiApi(prompt, currentCount, geminiOptions);

                if (!hasChanges
                    && responseData.Count != 0)
                {
                    hasChanges = true;
                }

                foreach (TranslatedRecord record in responseData)
                {
                    if (!record.NR.HasValue) continue;

                    TranslationRecord? originalRecord = allRecords.FirstOrDefault(r => r.NR == record.NR);

                    if (originalRecord is null) continue;

                    originalRecord.TranslatedText = record.TL;
                }
            }
            catch (Exception ex)
            {
                Log($"Stopping due to error during translation!{Environment.NewLine}{ex.Message}");
                break;
            }

            if (!Aborting)
                _window.StatusProgressBar.Value = 100 * (double)(i + list.Count) / totalMissing;

            await Task.Delay(RateLimitDelay);
        }

        if (!hasChanges) return;

        Log("Saving...");

        if (Aborting) return;

        try
        {
            await using StreamWriter writer = new(InputFile, append: false, Encoding.UTF8);
            await using CsvWriter csv = new(writer, CsvConfig);
            csv.Context.RegisterClassMap(new DynamicMap(SourceHeader, TargetHeader));
            await csv.WriteRecordsAsync(allRecords);
            Logger.Log("File saved!");
        }
        catch (Exception ex)
        {
            Log($"Error saving CSV file: {ex.Message}");
        }

        return;

        void Log(string message)
        {
            Logger.Log(message, currentCount);
        }
    }

    private static JsonSerializerOptions GetRequestOptions()
        => new(Options)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { typeInfo =>
                {
                    if (typeInfo.Type != typeof(TranslationRecord)) return;

                    foreach (JsonPropertyInfo property in typeInfo.Properties)
                    {
                        // ReSharper disable once ConvertIfStatementToSwitchStatement
                        if (property.Name == nameof(TranslationRecord.OriginalText))
                        {
                            property.Name = SourceHeader;
                        }
                        else if (property.Name == nameof(TranslationRecord.TranslatedText))
                        {
                            property.Name = TargetHeader;
                        }
                    }
                }}
            }
        };

    private static JsonSerializerOptions GetGeminiOptions()
        => new(Options)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    typeInfo =>
                    {
                        if (typeInfo.Type != typeof(TranslatedRecord)) return;

                        foreach (JsonPropertyInfo property in typeInfo.Properties)
                        {
                            if (property.Name == nameof(TranslatedRecord.TL))
                            {
                                property.Name = TargetHeader;
                            }
                        }
                    }
                }
            }
        };

    private static async Task<List<TranslatedRecord>> CallGeminiApi(string prompt, int currentCount, JsonSerializerOptions requestOptions)
    {
        Client client = new(
            apiKey: ApiKey,
            httpOptions: new HttpOptions { Timeout = 1000 * 60 * 30 }); // 30 minutes

        Schema recordSchema = new()
        {
            Properties = new Dictionary<string, Schema>
            {
                ["NR"] = new() { Type = Type.INTEGER },
                [TargetLanguage] = new() { Type = Type.STRING }
            },
            Required = ["NR", TargetLanguage],
        };

        Schema batchSchema = new()
        {
            Type = Type.OBJECT,
            Properties = new Dictionary<string, Schema>
            {
                ["d"] = new()
                {
                    Type = Type.ARRAY,
                    Items = recordSchema
                }
            },
            Required = ["d"]
        };

        GenerateContentConfig generateContentConfig = new()
        {
            ResponseMimeType = "application/json",
            ResponseJsonSchema = batchSchema,
            //CachedContent =
            ThinkingConfig = new ThinkingConfig
            {
                IncludeThoughts = false,
                ThinkingBudget = ThinkingTokens,
            },
            SafetySettings = SafetySettings,
        };

        GenerateContentResponse response = await client.Models.GenerateContentAsync(
            model: ModelName,
            contents: prompt,
            config: generateContentConfig);

        string responseText = response.Candidates
            ?.FirstOrDefault()
            ?.Content
            ?.Parts
            ?.FirstOrDefault()
            ?.Text ?? string.Empty;

        string text2 = responseText[..Math.Min(responseText.Length, 500)];
        const int responseDisplayLength = 500;
        if (responseText.Length > responseDisplayLength)
            text2 += "...";
        Logger.Debug($"Raw response (first {responseDisplayLength} characters):{Environment.NewLine}{text2}", currentCount);

        try
        {
            if (response.UsageMetadata != null)
            {
                Logger.Debug($"Token usage"
                        + $"{Environment.NewLine}    Input: {response.UsageMetadata.PromptTokenCount}"
                        + $"{Environment.NewLine}    Thoughts: {response.UsageMetadata.ThoughtsTokenCount}"
                        + $"{Environment.NewLine}    Output: {response.UsageMetadata.CandidatesTokenCount}",
                    currentCount);
            }

            TranslationBatch? batchResult = JsonSerializer
                .Deserialize<TranslationBatch>(responseText, requestOptions);

            return batchResult?.D ?? [];
        }
        catch (JsonException)
        {
            Log($"JSON Deserialization Error. Response might be truncated. Length: {responseText.Length}");
            Log($"Finish reason: {response.Candidates?.FirstOrDefault()?.FinishReason}");

            throw;
        }

        void Log(string message)
        {
            Logger.Log(message, currentCount);
        }
    }

    private sealed class DynamicMap : ClassMap<TranslationRecord>
    {
        public DynamicMap(string sourceColumn, string targetColumn)
        {
            AutoMap(CultureInfo.InvariantCulture);
            Map(m => m.OriginalText).Name(sourceColumn);
            Map(m => m.TranslatedText).Name(targetColumn).Optional();
        }
    }
}
