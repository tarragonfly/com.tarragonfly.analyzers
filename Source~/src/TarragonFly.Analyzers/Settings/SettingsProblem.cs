using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Settings
{
    // A line of the settings file that could not be used, reported as TF0000.
    public readonly struct SettingsProblem
    {
        public SettingsProblem(TextSpan span, string message)
        {
            Span = span;
            Message = message;
        }

        public TextSpan Span { get; }
        public string Message { get; }
    }
}