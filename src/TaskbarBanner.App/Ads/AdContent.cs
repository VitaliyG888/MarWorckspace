namespace TaskbarBanner.App.Ads;

public sealed record AdContent(
    string BrandName,
    string Headline,
    string Tagline,
    string LogoText,
    string? WebsiteUrl = null,
    string? LogoImageUrl = null);
