# Gothic Translator
A small Avalonia-based desktop application to automatically translate CSV files obtained from [Easy Gothic Mod Translator](https://worldofplayers.ru/threads/41696/) using the Gemini AI.  
Newer models like Gemini 3 Pro can process many lines at once for better context-awareness, are familiar with basic Gothic terms and can even use Google search. This makes the results much better than the traditional Google Translate approach.

## Requirements
- Windows (Avalonia is multi-platform, but I have not personally tested running it on Linux or macOS.)
- [.NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)
- A [Google Gemini API Key](https://aistudio.google.com/) (The free tier is sufficient for small mods.)
