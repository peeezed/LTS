using MudBlazor;

namespace LTS.Web.Components.Layout;

/// <summary>
/// The LTS palette. Status and performance colours are defined here rather than inline so a
/// "Late" row looks the same on the grids, the details page and the dashboard.
/// </summary>
public static class LtsTheme
{
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1c3f60",
            Secondary = "#3f7cac",
            AppbarBackground = "#1c3f60",
            Background = "#f6f7f9",
            DrawerBackground = "#ffffff",
            Success = "#2e7d32",
            Warning = "#ed6c02",
            Error = "#c62828",
            Info = "#0277bd"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#5b9bd5",
            Secondary = "#8ab6d6",
            AppbarBackground = "#12283c",
            Success = "#66bb6a",
            Warning = "#ffa726",
            Error = "#ef5350",
            Info = "#29b6f6"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            DrawerWidthLeft = "240px",
            DrawerMiniWidthLeft = "60px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontSize = "0.875rem" }
        }
    };
}
