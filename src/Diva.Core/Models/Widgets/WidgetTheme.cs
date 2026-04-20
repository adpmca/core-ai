namespace Diva.Core.Models.Widgets;

public record WidgetTheme
{
    // Surfaces
    public string Background { get; init; } = "#ffffff";
    public string Surface { get; init; } = "#f9fafb";
    public string Border { get; init; } = "#e5e7eb";

    // Brand / accent
    public string Primary { get; init; } = "#6366f1";
    public string PrimaryText { get; init; } = "#ffffff";

    // Typography
    public string Text { get; init; } = "#111827";
    public string TextMuted { get; init; } = "#6b7280";
    public string FontFamily { get; init; } = "system-ui, sans-serif";
    public string FontSize { get; init; } = "14px";

    // Agent bubble
    public string AgentBubbleBg { get; init; } = "#f3f4f6";
    public string AgentBubbleText { get; init; } = "#111827";

    // Header bar
    public string HeaderBg { get; init; } = "#6366f1";
    public string HeaderText { get; init; } = "#ffffff";

    // Input bar
    public string InputBg { get; init; } = "#ffffff";
    public string InputBorder { get; init; } = "#d1d5db";
    public string InputText { get; init; } = "#111827";

    // Launcher button size (px)
    public int LauncherSize { get; init; } = 56;

    /// <summary>Informational: "light" | "dark" | "custom". Not enforced server-side.</summary>
    public string? Preset { get; init; }

    public static WidgetTheme Light => new() { Preset = "light" };

    public static WidgetTheme Dark => new()
    {
        Background = "#1f2937",
        Surface = "#111827",
        Border = "#374151",
        Primary = "#818cf8",
        PrimaryText = "#ffffff",
        Text = "#f9fafb",
        TextMuted = "#9ca3af",
        AgentBubbleBg = "#374151",
        AgentBubbleText = "#f9fafb",
        HeaderBg = "#111827",
        HeaderText = "#f9fafb",
        InputBg = "#1f2937",
        InputBorder = "#4b5563",
        InputText = "#f9fafb",
        Preset = "dark"
    };
}
