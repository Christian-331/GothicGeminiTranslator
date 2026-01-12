using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace GothicTranslator;

public partial class MainWindow : Window
{
    private const string SettingsFile = "settings.json";

    private volatile bool _isDebugEnabled;

    public bool Debug => _isDebugEnabled;

    public MainWindow()
    {
        InitializeComponent();
        Logger.SetBoxes(TxtLog, this);
        ApiHandler.SetMainWindow(this);
        LoadSettings();
    }

    private static void SelectItem(ComboBox comboBox, string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return;

        object? item = comboBox.Items
            .FirstOrDefault(i => i?.ToString() is string itemText
                && itemText.Equals(modelName, StringComparison.OrdinalIgnoreCase));

        if (item is not null)
            comboBox.SelectedItem = item;
        else
            comboBox.Text = modelName;
    }

    public void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;

            string json = File.ReadAllText(SettingsFile);
            Settings? settings = JsonSerializer.Deserialize<Settings>(json);

            if (settings is null) return;

            CheckBoxDebug.IsChecked = settings.ShowDebug;

            (TextBox, string)[] textBoxes = [
                (TxtApiKey, settings.ApiKey),
                (TxtOutputTokens, settings.OutputTokens),
                (TxtTokenFactor, settings.TokenFactor),
                (TxtThinkingTokens, settings.ThinkingTokens),
                (TxtFilePath, settings.InputFile),
                (TxtDictionaryPath, settings.DictionaryFile),
            ];
            foreach ((TextBox textBox, string setting) in textBoxes)
            {
                if (!string.IsNullOrWhiteSpace(setting))
                    textBox.Text = setting;
            }

            (ComboBox, string)[] comboBoxes = [
                (ComboModel, settings.ModelName),
                (ComboSourceLanguage, settings.SourceLanguage),
                (ComboSourceHeader, settings.SourceHeader),
                (ComboTargetLanguage, settings.TargetLanguage),
                (ComboTargetHeader, settings.TargetHeader),
            ];
            foreach ((ComboBox comboBox, string setting) in comboBoxes)
            {
                if (!string.IsNullOrWhiteSpace(setting))
                    SelectItem(comboBox, setting);
            }
        }
        catch (Exception e)
        {
            Logger.Log($"Error while parsing settings file: {e.Message}");
        }
    }

    public void SaveSettings()
    {
        try
        {
            Settings settings = new()
            {
                ApiKey = TxtApiKey.Text ?? string.Empty,
                ModelName = ComboModel.Text ?? string.Empty,
                OutputTokens = TxtOutputTokens.Text ?? string.Empty,
                TokenFactor = TxtTokenFactor.Text ?? string.Empty,
                ThinkingTokens = TxtThinkingTokens.Text ?? string.Empty,
                InputFile = TxtFilePath.Text ?? string.Empty,
                DictionaryFile = TxtDictionaryPath.Text ?? string.Empty,
                ShowDebug = CheckBoxDebug.IsChecked ?? false,
                SourceLanguage = ComboSourceLanguage.Text ?? string.Empty,
                SourceHeader = ComboSourceHeader.Text ?? string.Empty,
                TargetLanguage = ComboTargetLanguage.Text ?? string.Empty,
                TargetHeader = ComboTargetHeader.Text ?? string.Empty,
            };
            string json = JsonSerializer.Serialize(settings);

            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception e)
        {
            Logger.Log($"Error while saving settings file: {e.Message}");
        }
    }

    private async void BtnBrowse_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            TopLevel? topLevel = GetTopLevel(this);
            if (topLevel is null) return;

            FilePickerOpenOptions options = new()
            {
                Title = "Open CSV file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("CSV files") { Patterns = ["*.csv"] }
                ]
            };
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider
                .OpenFilePickerAsync(options);

            if (files.Count > 0)
            {
                TxtFilePath.Text = files[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error while browsing for CSV file: {ex.Message}");
        }
    }

    private async void BtnStart_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();

            string? apiKey = TxtApiKey.Text;
            string? inputFile = TxtFilePath.Text;
            string? modelName = ComboModel.Text;
            string? sourceLanguage = ComboSourceLanguage.Text;
            string? sourceHeader = ComboSourceHeader.Text;
            string? targetLanguage = ComboTargetLanguage.Text;
            string? targetHeader = ComboTargetHeader.Text;

            List<string> errorMessages = [];
            if (string.IsNullOrWhiteSpace(inputFile) || !File.Exists(inputFile))
                errorMessages.Add("Error: Please select a valid CSV file.");
            else
                ApiHandler.InputFile = inputFile;

            if (string.IsNullOrWhiteSpace(apiKey))
                errorMessages.Add("Error: Please specify an API key.");
            else
                ApiHandler.ApiKey = apiKey;

            if (string.IsNullOrWhiteSpace(modelName))
                errorMessages.Add("Error: Please specify a model name.");
            else
                ApiHandler.ModelName = modelName;

            if (!int.TryParse(TxtOutputTokens.Text, out int outputTokens)
                || outputTokens <= 0)
                errorMessages.Add("Error: Please specify a positive integer output token size.");
            else
                ApiHandler.OutputTokens = outputTokens;

            if (!float.TryParse(TxtTokenFactor.Text, out float tokenFactor)
                || tokenFactor <= 0)
                errorMessages.Add("Error: Please specify a positive token factor.");
            else
                ApiHandler.TokenFactor = tokenFactor;

            if (!int.TryParse(TxtThinkingTokens.Text, out int thinkingTokens)
                || thinkingTokens < 0)
                errorMessages.Add("Error: Please specify a non-negative integer thinking token size.");
            else if (thinkingTokens >= outputTokens)
                errorMessages.Add("Error: The output token size must be greater than the thinking token size.");
            // ReSharper disable once ConvertIfStatementToSwitchStatement
            else if (thinkingTokens > 24_576 && modelName is not null && modelName.StartsWith("gemini-2.5-flash", StringComparison.OrdinalIgnoreCase))
                errorMessages.Add("Error: Gemini 2.5 Flash is limited to 24576 thinking tokens!");
            else if (thinkingTokens > 32_768 && modelName is not null && modelName.Equals("gemini-2.5-pro", StringComparison.OrdinalIgnoreCase))
                errorMessages.Add("Error: Gemini 2.5 Flash is limited to 32768 thinking tokens!");
            else
                ApiHandler.ThinkingTokens = thinkingTokens;

            if (string.IsNullOrWhiteSpace(sourceLanguage))
                errorMessages.Add("Error: Please specify a source language.");
            else
                ApiHandler.SourceLanguage = sourceLanguage;

            if (string.IsNullOrWhiteSpace(sourceHeader))
                errorMessages.Add("Error: Please specify a source header.");
            else
                ApiHandler.SourceHeader = sourceHeader;

            if (string.IsNullOrWhiteSpace(targetLanguage))
                errorMessages.Add("Error: Please specify a target language.");
            else
                ApiHandler.TargetLanguage = targetLanguage;

            if (string.IsNullOrWhiteSpace(targetHeader))
                errorMessages.Add("Error: Please specify a target header.");
            else
                ApiHandler.TargetHeader = targetHeader;

            if (sourceHeader == targetHeader)
                errorMessages.Add("Error: Source and target header must be different.");

            if (errorMessages.Count != 0)
            {
                Logger.Log(string.Join(Environment.NewLine, errorMessages));
                return;
            }

            ApiHandler.DictionaryFile = TxtDictionaryPath.Text ?? string.Empty;
            ApiHandler.Aborting = false;
            ApiHandler.Stopping = false;
            BtnStart.IsEnabled = false;
            TxtLog.Clear();
            StatusProgressBar.Value = 0;

            try
            {
                await ApiHandler.RunTranslationProcess();
            }
            catch (Exception ex)
            {
                Logger.Log($"CRITICAL ERROR: {ex.Message}");
            }
            finally
            {
                Reset();
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error while trying to translate: {ex.Message}");
        }
    }

    private void MainWinow_Closing(object? sender, WindowClosingEventArgs e)
    {
        SaveSettings();
    }

    private void BtnAbort_Click(object? sender, RoutedEventArgs e)
    {
        ApiHandler.Aborting = true;
        Reset();
        TxtLog.Clear();
    }

    private void BtnSop_Click(object? sender, RoutedEventArgs e)
    {
        ApiHandler.Stopping = true;
    }

    private void Reset()
    {
        BtnStart.IsEnabled = BtnStop.IsEnabled = BtnAbort.IsEnabled = true;
        StatusProgressBar.Value = 0;
    }

    private void CheckBoxDebug_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        _isDebugEnabled = CheckBoxDebug.IsChecked == true;
    }

    private static void SynchLanguage(ComboBox? changed, ComboBox? target)
    {
        if (changed is not null
            && target is not null)
            target.SelectedIndex = changed.SelectedIndex;
    }

    private void ComboTargetLanguage_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => SynchLanguage(ComboTargetLanguage, ComboTargetHeader);

    private void ComboTargetHeader_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => SynchLanguage(ComboTargetHeader, ComboTargetLanguage);

    private void ComboSourceLanguage_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => SynchLanguage(ComboSourceLanguage, ComboSourceHeader);

    private void ComboSourceHeader_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => SynchLanguage(ComboSourceHeader, ComboSourceLanguage);

    private async void BtnBrowseDictionary_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            TopLevel? topLevel = GetTopLevel(this);
            if (topLevel is null) return;

            FilePickerOpenOptions options = new()
            {
                Title = "Open dictionary file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Text files") { Patterns = ["*.txt"] }
                ]
            };
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider
                .OpenFilePickerAsync(options);

            if (files.Count > 0)
            {
                TxtDictionaryPath.Text = files[0].Path.LocalPath;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error while browsing for dictionary file: {ex.Message}");
        }
    }

    private void BtnClearDictionary_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            TxtDictionaryPath.Text = string.Empty;
        }
        catch (Exception ex)
        {
            Logger.Log($"Error while trying to clear dictionary file path: {ex.Message}");
        }
    }
}
