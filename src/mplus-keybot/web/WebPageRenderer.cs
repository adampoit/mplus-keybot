using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Http;

public static class WebPageRenderer
{
	private static readonly string[] s_avatarColors = new[]
	{
		"#e74c3c", "#e67e22", "#f1c40f", "#2ecc71", "#1abc9c",
		"#3498db", "#9b59b6", "#34495e", "#16a085", "#27ae60",
		"#2980b9", "#8e44ad", "#2c3e50", "#f39c12", "#d35400",
		"#c0392b", "#7f8c8d", "#2ecc71"
	};

	public static IResult RenderPage(
		WebUrlBuilder urls,
		string title,
		string body,
		bool isAuthenticated = false,
		HttpStatusCode statusCode = HttpStatusCode.OK,
		bool isDevelopment = false)
	{
		var html = BuildPage(urls, title, body, isAuthenticated, isDevelopment);
		return Results.Content(html, "text/html; charset=utf-8", statusCode: (int)statusCode);
	}

	public static string Html(string value) => HtmlEncoder.Default.Encode(value);

	public static string AvatarHtml(string name, string? renderUrl = null)
	{
		var initials = GetInitials(name);
		var color = GetAvatarColor(name);

		if (string.IsNullOrWhiteSpace(renderUrl))
		{
			var style = $"background-color: {color};";
			return $"<div class=\"avatar\" style=\"{Html(style)}\" aria-label=\"{Html(name)}\">{Html(initials)}</div>";
		}

		return $$"""
<div class="avatar-wrapper">
  <img src="{{Html(renderUrl)}}" alt="" class="avatar-img" loading="lazy" onerror="this.style.display='none';this.nextElementSibling.style.display='flex';">
  <div class="avatar-fallback" style="background-color: {{Html(color)}};display:none">{{Html(initials)}}</div>
</div>
""";
	}

	public static string RenderCharacterCard(
		string checkboxValue,
		string name,
		string realmDisplayName,
		string region,
		bool isChecked,
		string? renderUrl = null,
		int? level = null,
		int? maxLevel = null)
	{
		var checkedAttr = isChecked ? " checked" : string.Empty;
		var checkedClass = isChecked ? " checked" : string.Empty;
		var levelBadge = level.HasValue ? $"<span class=\"level-badge{LevelBadgeClass(level.Value, maxLevel)}\">{level.Value}</span>" : string.Empty;
		var dataAttrs = $"data-name=\"{Html(name)}\" data-realm=\"{Html(realmDisplayName)}\" data-level=\"{level?.ToString() ?? "0"}\" data-region=\"{Html(region)}\"";

		return $$"""
<label class="character-card{{checkedClass}}" {{dataAttrs}}>
  <input type="checkbox" name="characters" value="{{Html(checkboxValue)}}"{{checkedAttr}}>
  {{AvatarHtml(name, renderUrl)}}
  <div class="character-info">
    <div class="character-name">{{Html(name)}} {{levelBadge}}</div>
    <div class="character-meta">{{Html(realmDisplayName)}} · {{Html(region.ToUpperInvariant())}}</div>
  </div>
  <div class="check-indicator">✓</div>
</label>
""";
	}

	public static string RenderCharacterReadonlyCard(
		string name,
		string realmDisplayName,
		string region,
		string? renderUrl = null,
		int? level = null,
		int? maxLevel = null)
	{
		var levelBadge = level.HasValue ? $"<span class=\"level-badge{LevelBadgeClass(level.Value, maxLevel)}\">{level.Value}</span>" : string.Empty;

		return $$"""
<div class="character-card readonly">
  {{AvatarHtml(name, renderUrl)}}
  <div class="character-info">
    <div class="character-name">{{Html(name)}} {{levelBadge}}</div>
    <div class="character-meta">{{Html(realmDisplayName)}} · {{Html(region.ToUpperInvariant())}}</div>
  </div>
</div>
""";
	}

	public static string RenderCharacterHomeCard(
		string name,
		string realmDisplayName,
		string region,
		string? renderUrl = null,
		int? level = null,
		int? maxLevel = null,
		bool isErroring = false,
		double currentScore = 0,
		DateTime? lastCheckedAt = null,
		IReadOnlyList<(string DungeonName, int KeyLevel)>? dungeonAchievements = null)
	{
		var levelBadge = level.HasValue ? $"<span class=\"level-badge{LevelBadgeClass(level.Value, maxLevel)}\">{level.Value}</span>" : string.Empty;
		var errorBadge = isErroring ? "<span class=\"error-badge\" title=\"Character not found on Raider.IO\">⚠</span>" : string.Empty;
		var scoreBadge = currentScore > 0 ? $"<span class=\"character-score-plain\">🏆 {currentScore:F0}</span>" : string.Empty;

		var dungeonBadgesHtml = new StringBuilder();
		if (dungeonAchievements is not null && dungeonAchievements.Count > 0)
		{
			dungeonBadgesHtml.AppendLine("<div class=\"dungeon-badges\">");
			foreach (var (dungeonName, keyLevel) in dungeonAchievements)
			{
				var levelClass = keyLevel >= 10 ? "high" : keyLevel >= 5 ? "med" : "low";
				var shortName = GetDungeonShortName(dungeonName);
				dungeonBadgesHtml.AppendLine($$"""
<div class="dungeon-badge" title="{{Html(dungeonName)}} — +{{keyLevel}}">
  <span class="level {{levelClass}}">+{{keyLevel}}</span>
  <span class="name">{{Html(shortName)}}</span>
</div>
""");
			}
			dungeonBadgesHtml.AppendLine("</div>");
		}

		var (lastCheckedText, dotClass) = FormatLastChecked(lastCheckedAt);
		var statusLine = isErroring
			? "<div class=\"character-status error\">Not found on Raider.IO</div>"
			: "";

		return $$"""
<div class="character-row">
  <div class="character-row-avatar">
    {{AvatarHtml(name, renderUrl)}}
  </div>
  <div class="character-row-body">
    <div class="character-row-identity">
      <div class="character-row-name">
        {{Html(name)}} {{levelBadge}} {{errorBadge}} {{scoreBadge}}
      </div>
      <div class="character-meta">{{Html(realmDisplayName)}} · {{Html(region.ToUpperInvariant())}}</div>
      {{statusLine}}
    </div>
    {{dungeonBadgesHtml}}
    <div class="character-row-footer">
      <span class="last-checked"><span class="dot {{dotClass}}"></span> {{lastCheckedText}}</span>
    </div>
  </div>
</div>
""";
	}

	private static string BuildPage(WebUrlBuilder urls, string title, string body, bool isAuthenticated, bool isDevelopment)
	{
		var navLinks = new StringBuilder();
		navLinks.AppendLine($"      <a href=\"{Html(urls.BuildPublicUrl("/"))}\">Home</a>");
		if (isAuthenticated)
		{
			navLinks.AppendLine($"      <a href=\"{Html(urls.BuildPublicUrl("/follow/characters"))}\">Manage Characters</a>");
			navLinks.AppendLine($"      <a href=\"{Html(urls.BuildPublicUrl("/signout"))}\">Sign Out</a>");
		}
		else
		{
			navLinks.AppendLine($"      <a href=\"{Html(urls.BuildPublicUrl("/signin"))}\">Sign In</a>");
		}
		if (isDevelopment)
		{
			navLinks.AppendLine($"      <a href=\"{Html(urls.BuildPublicUrl("/dev"))}\">Dev Tools</a>");
		}

		return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{{Html(title)}} - mplus-keybot</title>
  <style>
    :root {
      --bg-primary: #0f0f1a;
      --bg-secondary: #1a1a2e;
      --bg-card: #16213e;
      --bg-hover: #1e2a4a;
      --border-color: #2d2d44;
      --text-primary: #e0e0e0;
      --text-secondary: #a0a0b0;
      --accent-gold: #c9a227;
      --accent-gold-light: #ffd700;
      --accent-gold-dim: rgba(201, 162, 39, 0.15);
      --accent-blue: #4a90d9;
      --success: #4caf50;
      --danger: #f44336;
    }

    * { box-sizing: border-box; }

    body {
      font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
      background: var(--bg-primary);
      color: var(--text-primary);
      margin: 0;
      min-height: 100vh;
      line-height: 1.6;
      display: flex;
      flex-direction: column;
    }

    .navbar {
      background: var(--bg-secondary);
      border-bottom: 1px solid var(--border-color);
      padding: 0 1.5rem;
      flex-shrink: 0;
    }
    .navbar-inner {
      max-width: 64rem;
      margin: 0 auto;
      display: flex;
      align-items: center;
      justify-content: space-between;
      height: 3.5rem;
    }
    .navbar-brand {
      font-weight: 700;
      font-size: 1.25rem;
      color: var(--accent-gold);
      text-decoration: none;
      display: flex;
      align-items: center;
      gap: 0.5rem;
      letter-spacing: -0.02em;
    }
    .navbar-brand:hover {
      color: var(--accent-gold-light);
    }
    .navbar-nav {
      display: flex;
      gap: 1.5rem;
      align-items: center;
    }
    .navbar-nav a {
      color: var(--text-secondary);
      text-decoration: none;
      font-size: 0.9rem;
      font-weight: 500;
      transition: color 0.2s;
    }
    .navbar-nav a:hover {
      color: var(--text-primary);
    }

    .main-content {
      max-width: 64rem;
      margin: 0 auto;
      padding: 2rem 1.5rem;
      width: 100%;
      flex: 1;
    }

    .page-header {
      margin-bottom: 2rem;
    }
    .page-header h1 {
      font-size: 1.75rem;
      font-weight: 700;
      margin: 0 0 0.5rem 0;
      color: var(--text-primary);
    }
    .page-header p {
      color: var(--text-secondary);
      margin: 0;
      font-size: 1rem;
    }

    h2 {
      font-size: 1.1rem;
      font-weight: 600;
      color: var(--text-secondary);
      margin: 2rem 0 1rem 0;
      padding-bottom: 0.5rem;
      border-bottom: 1px solid var(--border-color);
    }

    .card {
      background: var(--bg-card);
      border: 1px solid var(--border-color);
      border-radius: 0.75rem;
      padding: 1.5rem;
      margin-bottom: 1.5rem;
    }
    .card-title {
      font-size: 1.1rem;
      font-weight: 600;
      margin: 0 0 1rem 0;
      color: var(--text-primary);
    }

    .character-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
      gap: 1rem;
    }
    .character-card {
      background: var(--bg-secondary);
      border: 2px solid var(--border-color);
      border-radius: 0.75rem;
      padding: 1rem;
      display: flex;
      align-items: center;
      gap: 0.875rem;
      cursor: pointer;
      transition: border-color 0.2s, background 0.2s;
      position: relative;
    }
    .character-card:hover {
      border-color: var(--accent-gold);
      background: var(--bg-hover);
    }
    .character-card input {
      position: absolute;
      opacity: 0;
    }
    .character-card.checked,
    .character-card:has(input:checked) {
      border-color: var(--accent-gold);
      background: var(--accent-gold-dim);
    }
    .character-card .check-indicator {
      margin-left: auto;
      width: 1.5rem;
      height: 1.5rem;
      border-radius: 50%;
      background: var(--accent-gold);
      color: var(--bg-primary);
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 0.875rem;
      font-weight: 700;
      opacity: 0;
      transition: opacity 0.2s;
    }
    .character-card.checked .check-indicator,
    .character-card:has(input:checked) .check-indicator {
      opacity: 1;
    }
    .character-card.readonly {
      cursor: default;
      border: 1px solid var(--border-color);
    }
    .character-card.readonly:hover {
      border-color: var(--border-color);
      background: var(--bg-secondary);
    }
    .character-row {
      display: flex;
      align-items: flex-start;
      gap: 1rem;
      padding: 1.25rem 0;
      border-bottom: 1px solid var(--border-color);
      transition: background 0.15s;
      border-left: 3px solid transparent;
      padding-left: 1rem;
    }
    .character-row:hover {
      background: rgba(255,255,255,0.02);
      border-left-color: var(--accent-gold);
    }
    .character-row-avatar {
      flex-shrink: 0;
    }
    .character-row-body {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }
    .character-row-identity {
      display: flex;
      flex-direction: column;
      gap: 0.15rem;
    }
    .character-row-name {
      font-weight: 700;
      font-size: 1.15rem;
      color: var(--text-primary);
      display: flex;
      align-items: center;
      gap: 0.4rem;
      flex-wrap: wrap;
    }
    .character-score-plain {
      font-weight: 700;
      font-size: 0.95rem;
      color: var(--accent-gold-light);
      margin-left: 0.35rem;
    }
    .character-row-footer {
      display: flex;
      align-items: center;
      justify-content: flex-end;
      font-size: 0.75rem;
      color: var(--text-secondary);
      margin-top: 0.25rem;
    }
    .last-checked {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
    }
    .last-checked .dot {
      width: 0.35rem;
      height: 0.35rem;
      border-radius: 50%;
      background: var(--success);
      display: inline-block;
      flex-shrink: 0;
      opacity: 0.6;
    }
    .last-checked .dot.ok {
      background: var(--success);
    }
    .last-checked .dot.warn {
      background: var(--accent-gold);
    }
    .last-checked .dot.stale {
      background: var(--danger);
    }
    .dungeon-badges {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      padding: 0.25rem 0;
    }
    .dungeon-badge {
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 0.5rem 0.6rem;
      background: var(--bg-secondary);
      border: 1px solid var(--border-color);
      border-radius: 0.5rem;
      min-width: 3.5rem;
      transition: all 0.15s;
    }
    .dungeon-badge:hover {
      border-color: var(--accent-gold);
      background: var(--bg-hover);
    }
    .dungeon-badge .level {
      font-size: 1rem;
      font-weight: 800;
      line-height: 1;
    }
    .dungeon-badge .level.high {
      color: var(--accent-gold-light);
    }
    .dungeon-badge .level.med {
      color: var(--accent-blue);
    }
    .dungeon-badge .level.low {
      color: var(--text-secondary);
    }
    .dungeon-badge .name {
      font-size: 0.7rem;
      color: var(--text-secondary);
      text-align: center;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      max-width: 5rem;
      margin-top: 0.25rem;
      line-height: 1.1;
    }
    .home-grid {
      display: flex;
      flex-direction: column;
      gap: 0;
      max-width: 800px;
      margin: 0 auto;
    }
    .character-status {
      font-size: 0.75rem;
      margin-top: 0.35rem;
      font-weight: 600;
    }
    .character-status.ok {
      color: var(--success);
    }
    .character-status.error {
      color: var(--danger);
    }
    .error-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 1.25rem;
      height: 1.25rem;
      border-radius: 50%;
      background: var(--danger);
      color: white;
      font-size: 0.7rem;
      margin-left: 0.35rem;
      vertical-align: middle;
    }

    .avatar {
      width: 2.75rem;
      height: 2.75rem;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 1rem;
      color: white;
      flex-shrink: 0;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      border: 2px solid rgba(255,255,255,0.1);
    }
    .avatar-wrapper {
      width: 2.75rem;
      height: 2.75rem;
      border-radius: 50%;
      overflow: hidden;
      flex-shrink: 0;
      border: 2px solid rgba(255,255,255,0.1);
      position: relative;
    }
    .character-row-avatar .avatar,
    .character-row-avatar .avatar-wrapper {
      width: 3rem;
      height: 3rem;
    }
    .avatar-img {
      width: 100%;
      height: 100%;
      object-fit: cover;
      display: block;
    }
    .avatar-fallback {
      width: 100%;
      height: 100%;
      display: none;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 1rem;
      color: white;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      position: absolute;
      top: 0;
      left: 0;
    }

    .level-badge {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-width: 1.5rem;
      height: 1.25rem;
      padding: 0 0.4rem;
      border-radius: 0.25rem;
      font-size: 0.7rem;
      font-weight: 700;
      color: var(--text-secondary);
      background: var(--bg-secondary);
      border: 1px solid var(--border-color);
      margin-left: 0.35rem;
      vertical-align: middle;
      line-height: 1;
    }
    .level-badge.level-max {
      color: var(--bg-primary);
      background: var(--accent-gold);
      border-color: var(--accent-gold);
    }

    .character-info {
      min-width: 0;
      flex: 1;
    }
    .character-name {
      font-weight: 600;
      font-size: 0.95rem;
      color: var(--text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .character-row-name {
      font-weight: 700;
      font-size: 1.15rem;
      color: var(--text-primary);
      display: flex;
      align-items: center;
      gap: 0.4rem;
      flex-wrap: wrap;
    }
    .character-meta {
      font-size: 0.8rem;
      color: var(--text-secondary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .character-row .character-meta {
      font-size: 0.9rem;
    }

    .realm-group {
      margin-bottom: 2rem;
    }
    .realm-group h3 {
      font-size: 0.8rem;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--text-secondary);
      margin: 0 0 1rem 0;
      padding-bottom: 0.5rem;
      border-bottom: 1px solid var(--border-color);
    }

    .btn {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      padding: 0.625rem 1.25rem;
      border-radius: 0.5rem;
      font: inherit;
      font-size: 0.9rem;
      font-weight: 600;
      cursor: pointer;
      text-decoration: none;
      border: none;
      transition: all 0.2s;
    }
    .btn-primary {
      background: var(--accent-gold);
      color: #1a1a2e;
    }
    .btn-primary:hover {
      background: var(--accent-gold-light);
      opacity: 1;
    }
    .btn-secondary {
      background: var(--bg-secondary);
      color: var(--text-primary);
      border: 1px solid var(--border-color);
    }
    .btn-secondary:hover {
      background: var(--bg-hover);
      border-color: var(--accent-gold);
      opacity: 1;
    }
    .btn-ghost {
      background: transparent;
      color: var(--text-secondary);
      border: 1px solid var(--border-color);
      padding: 0.5rem 0.75rem;
      font-size: 0.85rem;
    }
    .btn-ghost:hover {
      color: var(--text-primary);
      border-color: var(--accent-gold);
    }
    .btn-ghost.active {
      color: var(--accent-gold);
      border-color: var(--accent-gold);
      background: var(--accent-gold-dim);
    }

    .toolbar {
      display: flex;
      gap: 1rem;
      align-items: center;
      flex-wrap: wrap;
      margin-bottom: 1.5rem;
      padding: 1rem;
      background: var(--bg-card);
      border: 1px solid var(--border-color);
      border-radius: 0.75rem;
    }
    .toolbar-group {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .toolbar-label {
      font-size: 0.85rem;
      color: var(--text-secondary);
      font-weight: 500;
    }
    .toolbar-input {
      background: var(--bg-secondary);
      border: 1px solid var(--border-color);
      border-radius: 0.5rem;
      padding: 0.5rem 0.75rem;
      color: var(--text-primary);
      font: inherit;
      font-size: 0.9rem;
      min-width: 14rem;
    }
    .toolbar-input:focus {
      outline: none;
      border-color: var(--accent-gold);
    }
    .toolbar-select {
      background: var(--bg-secondary);
      border: 1px solid var(--border-color);
      border-radius: 0.5rem;
      padding: 0.5rem 0.75rem;
      color: var(--text-primary);
      font: inherit;
      font-size: 0.9rem;
      cursor: pointer;
    }
    .toolbar-select:focus {
      outline: none;
      border-color: var(--accent-gold);
    }
    .toolbar-count {
      font-size: 0.85rem;
      color: var(--text-secondary);
      margin-left: auto;
    }
    .realm-group.hidden {
      display: none;
    }
    .character-card.hidden {
      display: none;
    }

    .status-badge {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      padding: 0.25rem 0.75rem;
      border-radius: 999px;
      font-size: 0.8rem;
      font-weight: 500;
    }
    .status-followed {
      background: rgba(76, 175, 80, 0.15);
      color: var(--success);
    }

    .alert {
      padding: 1rem 1.25rem;
      border-radius: 0.5rem;
      margin-bottom: 1.5rem;
    }
    .alert-info {
      background: rgba(74, 144, 217, 0.1);
      border: 1px solid rgba(74, 144, 217, 0.3);
      color: var(--accent-blue);
    }
    .alert-success {
      background: rgba(76, 175, 80, 0.1);
      border: 1px solid rgba(76, 175, 80, 0.3);
      color: var(--success);
    }
    .alert-error {
      background: rgba(244, 67, 54, 0.1);
      border: 1px solid rgba(244, 67, 54, 0.3);
      color: var(--danger);
    }

    .character-list {
      list-style: none;
      padding: 0;
      margin: 0;
    }
    .character-list li {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      padding: 0.625rem 0;
      border-bottom: 1px solid var(--border-color);
    }
    .character-list li:last-child {
      border-bottom: none;
    }

    .footer {
      max-width: 64rem;
      margin: 0 auto;
      padding: 1.5rem;
      border-top: 1px solid var(--border-color);
      color: var(--text-secondary);
      font-size: 0.8rem;
      text-align: center;
      flex-shrink: 0;
      width: 100%;
    }

    .empty-state {
      text-align: center;
      padding: 3rem 1rem;
      color: var(--text-secondary);
    }
    .empty-state-icon {
      font-size: 2.5rem;
      margin-bottom: 1rem;
      opacity: 0.5;
    }

    .hero {
      text-align: center;
      padding: 2rem 0 3rem;
    }
    .hero h1 {
      font-size: 2.5rem;
      font-weight: 800;
      color: var(--accent-gold);
      margin: 0 0 1rem 0;
      letter-spacing: -0.02em;
    }
    .hero p {
      font-size: 1.1rem;
      color: var(--text-secondary);
      max-width: 36rem;
      margin: 0 auto;
    }
    .hero-actions {
      margin-top: 2rem;
      display: flex;
      gap: 1rem;
      justify-content: center;
      flex-wrap: wrap;
    }

    .back-link {
      display: inline-flex;
      align-items: center;
      gap: 0.35rem;
      color: var(--text-secondary);
      text-decoration: none;
      font-size: 0.9rem;
      margin-bottom: 1.5rem;
      transition: color 0.2s;
    }
    .back-link:hover {
      color: var(--text-primary);
    }

    code {
      background: var(--bg-secondary);
      padding: 0.15rem 0.35rem;
      border-radius: 0.25rem;
      font-size: 0.9em;
      color: var(--accent-gold-light);
    }

    @media (max-width: 640px) {
      .character-grid {
        grid-template-columns: 1fr;
      }
      .home-grid {
        grid-template-columns: 1fr;
        max-width: none;
      }
      .hero h1 {
        font-size: 1.75rem;
      }
      .navbar-inner {
        height: 3rem;
      }
      .navbar-brand {
        font-size: 1.1rem;
      }
    }
  </style>
</head>
<body>
  <nav class="navbar">
    <div class="navbar-inner">
      <a class="navbar-brand" href="{{Html(urls.BuildPublicUrl("/"))}}">🔑 mplus-keybot</a>
      <div class="navbar-nav">
{{navLinks}}
      </div>
    </div>
  </nav>
  <main class="main-content">
{{body}}
  </main>
  <footer class="footer">
    mplus-keybot · WoW Mythic+ run tracker
  </footer>
  <script>
    document.querySelectorAll('.character-card input').forEach(function(input) {
      input.addEventListener('change', function() {
        this.closest('.character-card').classList.toggle('checked', this.checked);
      });
    });

    (function() {
      var filterInput = document.getElementById('char-filter');
      var sortSelect = document.getElementById('char-sort');
      var countLabel = document.getElementById('char-count');
      var form = document.querySelector('.character-grid')?.closest('form');
      if (!form) return;

      var allGroups = Array.from(form.querySelectorAll('.realm-group'));
      var firstGroup = allGroups[0];
      var firstGrid = firstGroup?.querySelector('.character-grid');
      var originalH3s = new Map();
      allGroups.forEach(function(g) {
        var h3 = g.querySelector('h3');
        if (h3) originalH3s.set(g, h3.textContent);
      });

      function getCards() {
        return form.querySelectorAll('.character-card');
      }

      function updateCount() {
        if (!countLabel) return;
        var visible = form.querySelectorAll('.character-card:not(.hidden)').length;
        countLabel.textContent = visible + ' character' + (visible === 1 ? '' : 's');
      }

      function filterCards() {
        var term = (filterInput ? filterInput.value : '').toLowerCase().trim();
        getCards().forEach(function(card) {
          var name = (card.getAttribute('data-name') || '').toLowerCase();
          var realm = (card.getAttribute('data-realm') || '').toLowerCase();
          var match = !term || name.indexOf(term) !== -1 || realm.indexOf(term) !== -1;
          card.classList.toggle('hidden', !match);
        });
        form.querySelectorAll('.realm-group').forEach(function(group) {
          var visible = group.querySelectorAll('.character-card:not(.hidden)').length;
          group.classList.toggle('hidden', visible === 0);
        });
        updateCount();
      }

      function restoreGroups() {
        allGroups.forEach(function(group) {
          group.classList.remove('hidden');
          var grid = group.querySelector('.character-grid');
          var realm = group.getAttribute('data-realm');
          var h3 = group.querySelector('h3');
          if (h3 && originalH3s.has(group)) h3.textContent = originalH3s.get(group);
          if (!grid || !realm) return;
          var cards = Array.from(form.querySelectorAll('.character-card[data-realm="' + realm + '"]'));
          cards.forEach(function(c) { grid.appendChild(c); });
        });
      }

      function sortCards() {
        var sort = sortSelect ? sortSelect.value : 'level';

        if (sort === 'name') {
          if (!firstGrid) return;
          restoreGroups();
          allGroups.forEach(function(g, i) {
            if (i !== 0) g.classList.add('hidden');
          });
          var h3 = firstGroup.querySelector('h3');
          if (h3) h3.textContent = 'All Characters';
          var allCards = Array.from(form.querySelectorAll('.character-card'));
          allCards.sort(function(a, b) {
            return a.getAttribute('data-name').localeCompare(b.getAttribute('data-name'));
          });
          allCards.forEach(function(c) { firstGrid.appendChild(c); });
        } else {
          restoreGroups();
          allGroups.forEach(function(group) {
            var grid = group.querySelector('.character-grid');
            if (!grid) return;
            var cards = Array.from(grid.querySelectorAll('.character-card'));
            cards.sort(function(a, b) {
              var la = parseInt(a.getAttribute('data-level') || '0', 10);
              var lb = parseInt(b.getAttribute('data-level') || '0', 10);
              if (la !== lb) return lb - la;
              return a.getAttribute('data-name').localeCompare(b.getAttribute('data-name'));
            });
            cards.forEach(function(c) { grid.appendChild(c); });
          });
        }
        filterCards();
      }

      if (filterInput) {
        filterInput.addEventListener('input', function() {
          filterCards();
        });
      }
      if (sortSelect) {
        sortSelect.addEventListener('change', function() {
          sortCards();
        });
      }
      updateCount();
    })();
  </script>
</body>
</html>
""";
	}

	private static string LevelBadgeClass(int level, int? maxLevel) => maxLevel.HasValue && level >= maxLevel.Value ? " level-max" : string.Empty;

	private static string GetInitials(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return "?";

		if (name.Length <= 2)
			return name;

		return name[..2];
	}

	private static string GetAvatarColor(string name)
	{
		var hash = name.GetHashCode(StringComparison.OrdinalIgnoreCase);
		var index = Math.Abs(hash) % s_avatarColors.Length;
		return s_avatarColors[index];
	}

	private static (string Text, string DotClass) FormatLastChecked(DateTime? lastCheckedAt)
	{
		if (lastCheckedAt is null)
			return ("never", "stale");

		var elapsed = DateTime.UtcNow - lastCheckedAt.Value;
		if (elapsed.TotalMinutes < 10)
			return ("just now", "ok");
		if (elapsed.TotalMinutes < 60)
			return ($"{(int)elapsed.TotalMinutes}m ago", "ok");
		if (elapsed.TotalHours < 24)
			return ($"{(int)elapsed.TotalHours}h ago", "warn");
		return ($"{(int)elapsed.TotalDays}d ago", "stale");
	}

	private static string GetDungeonShortName(string dungeonName)
	{
		if (string.IsNullOrWhiteSpace(dungeonName))
			return "?";

		if (dungeonName.Contains("Necrotic Wake", StringComparison.OrdinalIgnoreCase)) return "NW";
		if (dungeonName.Contains("Mists", StringComparison.OrdinalIgnoreCase)) return "MoTS";
		if (dungeonName.Contains("MOTHERLODE", StringComparison.OrdinalIgnoreCase)) return "ML";
		if (dungeonName.Contains("Stonevault", StringComparison.OrdinalIgnoreCase)) return "SV";
		if (dungeonName.Contains("City of Threads", StringComparison.OrdinalIgnoreCase)) return "CoT";
		if (dungeonName.Contains("Ara-Kara", StringComparison.OrdinalIgnoreCase)) return "AK";
		if (dungeonName.Contains("Dawnbreaker", StringComparison.OrdinalIgnoreCase)) return "DB";
		if (dungeonName.Contains("Grim Batol", StringComparison.OrdinalIgnoreCase)) return "GB";
		if (dungeonName.Contains("Siege", StringComparison.OrdinalIgnoreCase)) return "SoB";
		if (dungeonName.Contains("Murozond", StringComparison.OrdinalIgnoreCase)) return "MR";
		if (dungeonName.Contains("Cinderbrew", StringComparison.OrdinalIgnoreCase)) return "CB";
		if (dungeonName.Contains("Darkflame", StringComparison.OrdinalIgnoreCase)) return "DC";
		if (dungeonName.Contains("Priory", StringComparison.OrdinalIgnoreCase)) return "POT";
		if (dungeonName.Contains("Rookery", StringComparison.OrdinalIgnoreCase)) return "ROOK";
		if (dungeonName.Contains("Floodgate", StringComparison.OrdinalIgnoreCase)) return "FG";
		if (dungeonName.Contains("Theater", StringComparison.OrdinalIgnoreCase)) return "TOP";
		if (dungeonName.Contains("Streets", StringComparison.OrdinalIgnoreCase)) return "SoA";
		if (dungeonName.Contains("Gambit", StringComparison.OrdinalIgnoreCase)) return "SoG";
		if (dungeonName.Contains("Workshop", StringComparison.OrdinalIgnoreCase)) return "WORK";
		if (dungeonName.Contains("Junkyard", StringComparison.OrdinalIgnoreCase)) return "JY";
		if (dungeonName.Contains("Underrot", StringComparison.OrdinalIgnoreCase)) return "UR";
		if (dungeonName.Contains("Freehold", StringComparison.OrdinalIgnoreCase)) return "FH";
		if (dungeonName.Contains("Waycrest", StringComparison.OrdinalIgnoreCase)) return "WM";
		if (dungeonName.Contains("Atal", StringComparison.OrdinalIgnoreCase)) return "AD";
		if (dungeonName.Contains("Kings' Rest", StringComparison.OrdinalIgnoreCase)) return "KR";
		if (dungeonName.Contains("Shrine", StringComparison.OrdinalIgnoreCase)) return "SoS";
		if (dungeonName.Contains("Temple", StringComparison.OrdinalIgnoreCase)) return "ToS";
		if (dungeonName.Contains("Operation", StringComparison.OrdinalIgnoreCase)) return "OM";

		var skipWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "The", "of", "in", "a", "an", "and" };
		var words = dungeonName.Split([' ', '-', ',', '!'], StringSplitOptions.RemoveEmptyEntries);
		var letters = words
			.Where(w => !skipWords.Contains(w))
			.Select(w => char.ToUpperInvariant(w[0]))
			.Take(3);
		return new string(letters.ToArray());
	}
}
