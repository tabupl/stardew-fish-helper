using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace FishingPlanner;

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private readonly PerScreen<FishingService?> services = new();
    private readonly PerScreen<bool> dirty = new(() => true);
    private readonly PerScreen<int> pages = new();
    private readonly PerScreen<string> fingerprints = new(() => "");
    private Point? dragOffset;
    private Rectangle panel, previous, next;
    private readonly List<(Rectangle Bounds, Suggestion Fish)> rows = new();
    private FishingService Service => services.Value ??= new FishingService(Monitor);
    private bool Visible => config.Enabled && Context.IsWorldReady && Game1.displayHUD && !Game1.eventUp
        && Game1.activeClickableMenu == null && !Game1.fadeToBlack;

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>(); Normalize();
        helper.Events.GameLoop.GameLaunched += (_, _) => RegisterOptions();
        helper.Events.GameLoop.SaveLoaded += (_, _) => { Service.Reset(); dirty.Value = true; pages.Value = 0; };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => { services.Value?.Reset(); dirty.Value = true; rows.Clear(); };
        helper.Events.GameLoop.DayStarted += (_, _) => { Service.InvalidateMaps(); dirty.Value = true; };
        helper.Events.GameLoop.TimeChanged += (_, _) => dirty.Value = true;
        helper.Events.Player.Warped += (_, e) => { if (e.IsLocalPlayer) { Service.Reprioritize(e.NewLocation); Service.InvalidateMaps(); dirty.Value = true; pages.Value = 0; } };
        helper.Events.Content.AssetsInvalidated += (_, e) =>
        {
            if (e.NamesWithoutLocale.Any(p => p.IsEquivalentTo("Data/Fish") || p.IsEquivalentTo("Data/Locations")
                || p.IsEquivalentTo("Data/Objects") || p.StartsWith("Maps/")))
            { services.Value?.InvalidateMaps(); dirty.Value = true; }
        };
        helper.Events.GameLoop.OneSecondUpdateTicked += (_, _) =>
        {
            if (!Context.IsWorldReady || !config.Enabled) return;
            string now = $"{Game1.player.fishCaught.Count()}|{Game1.player.stats.Get("PreciseFishCaught")}|{Game1.player.FishingLevel}|{Game1.player.CurrentTool?.QualifiedItemId}|{Game1.player.mailReceived.Count}|{Game1.player.eventsSeen.Count}|{Game1.isRaining}|{Game1.player.LuckLevel}|{Game1.player.DailyLuck}|{(Game1.player.CurrentTool as StardewValley.Tools.FishingRod)?.GetBait()?.QualifiedItemId}|{(Game1.player.CurrentTool as StardewValley.Tools.FishingRod)?.GetBait()?.preservedParentSheetIndex.Value}|{(Game1.player.CurrentTool as StardewValley.Tools.FishingRod)?.HasCuriosityLure()}";
            if (now != fingerprints.Value) { fingerprints.Value = now; dirty.Value = true; }
        };
        helper.Events.GameLoop.UpdateTicked += (_, _) =>
        {
            if (!Context.IsWorldReady || !config.Enabled) return;
            if (dirty.Value)
            {
                try { Service.Start(config); dirty.Value = false; }
                catch (Exception ex) { dirty.Value = false; Monitor.Log($"Couldn't read fishing data: {ex}", LogLevel.Error); }
            }
            Service.Step();
            if (dragOffset is Point offset && Visible)
            {
                Point cursor = Cursor();
                config.X = Math.Clamp(cursor.X - offset.X, 0, Math.Max(0, Game1.uiViewport.Width - panel.Width));
                config.Y = Math.Clamp(cursor.Y - offset.Y, 0, Math.Max(0, Game1.uiViewport.Height - panel.Height));
            }
        };
        helper.Events.Display.RenderedHud += Draw;
        helper.Events.Input.ButtonsChanged += (_, _) =>
        {
            if (!Context.IsWorldReady || !config.Toggle.JustPressed()) return;
            config.Enabled = !config.Enabled; helper.Input.SuppressActiveKeybinds(config.Toggle);
            helper.WriteConfig(config); dirty.Value = true;
        };
        helper.Events.Input.ButtonPressed += OnButton;
        helper.Events.Input.ButtonReleased += (_, e) =>
        {
            if (e.Button == SButton.MouseLeft && dragOffset != null)
            { dragOffset = null; helper.WriteConfig(config); }
        };
        helper.Events.Input.MouseWheelScrolled += (_, e) =>
        {
            if (Visible && panel.Contains(Cursor())) { ChangePage(e.Delta > 0 ? -1 : 1); helper.Input.SuppressScrollWheel(); }
        };
        helper.ConsoleCommands.Add("fishing_planner", "Toggle the HUD, or use: fishing_planner refresh | report", (_, args) =>
        {
            if (args.FirstOrDefault() == "report")
            {
                if (!Context.IsWorldReady) { Monitor.Log("Load a farm first.", LogLevel.Info); return; }
                Helper.Data.WriteJsonFile("verification/latest-report.json", new
                {
                    Game = Game1.version, Player = Game1.player.Name, Time = Game1.timeOfDay,
                    Service.Scanning, Service.Fallback, Service.Remaining, Service.UnvisitedSkipped, config.OnlyVisitedModLocations,
                    Service.FailedLocations, Service.Suggestions,
                });
                Monitor.Log("Wrote verification/latest-report.json in the FishingPlanner mod folder.", LogLevel.Info);
            }
            else if (args.FirstOrDefault() == "refresh") { Service.InvalidateMaps(); dirty.Value = true; }
            else { config.Enabled = !config.Enabled; Helper.WriteConfig(config); dirty.Value = true; }
        });
    }

    private void Normalize()
    {
        config.Rows = Math.Clamp(config.Rows, 3, 8); config.Width = Math.Clamp(config.Width, 280, 600);
        config.FallbackLocations = FishingRules.WithRequiredFallbackLocations(config.FallbackLocations);
        config.ExcludedLocations ??= Array.Empty<string>(); config.LocationConditions ??= new();
    }
    private void RegisterOptions()
    {
        var api = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (api == null) return;
        api.Register(ModManifest, () => { config = new(); dirty.Value = true; },
            () => { Normalize(); Helper.WriteConfig(config); dirty.Value = true; });
        api.AddBoolOption(ModManifest, () => config.Enabled, v => config.Enabled = v, () => "Show fishing planner");
        api.AddKeybindList(ModManifest, () => config.Toggle, v => config.Toggle = v, () => "Toggle overlay");
        api.AddNumberOption(ModManifest, () => config.Rows, v => config.Rows = v, () => "Fish shown", min: 3, max: 8);
        api.AddNumberOption(ModManifest, () => config.Width, v => config.Width = v, () => "Panel width", min: 280, max: 600, interval: 10);
        api.AddBoolOption(ModManifest, () => config.OnlyVisitedModLocations, v => config.OnlyVisitedModLocations = v,
            () => "Only visited extra areas", () => "Avoid story-locked modded maps and dungeons. Main outdoor vanilla maps still appear before visiting. Disable to include unvisited areas.");
        api.AddNumberOption(ModManifest, () => config.X, v => config.X = v, () => "Panel X", min: 0, max: 3840);
        api.AddNumberOption(ModManifest, () => config.Y, v => config.Y = v, () => "Panel Y", min: 0, max: 2160);
    }
    private Point Cursor() => Helper.Input.GetCursorPosition().GetScaledScreenPixels().ToPoint();
    private int PageCount => Math.Max(1, (Service.Suggestions.Count + config.Rows - 1) / config.Rows);
    private void ChangePage(int amount) => pages.Value = (pages.Value + amount + PageCount) % PageCount;

    private void OnButton(object? sender, ButtonPressedEventArgs e)
    {
        if (!Visible || e.Button != SButton.MouseLeft) return;
        Point cursor = Cursor();
        if (!panel.Contains(cursor)) return;
        Helper.Input.Suppress(e.Button);
        if (previous.Contains(cursor)) { ChangePage(-1); return; }
        if (next.Contains(cursor)) { ChangePage(1); return; }
        if (cursor.Y < panel.Y + 42) { dragOffset = cursor - panel.Location; return; }
        var row = rows.FirstOrDefault(p => p.Bounds.Contains(cursor));
        if (row.Fish != null) Game1.activeClickableMenu = new FishDetailsMenu(row.Fish, Service.Fallback);
    }

    private void Draw(object? sender, RenderedHudEventArgs e)
    {
        rows.Clear();
        if (!Visible) return;
        int width = Math.Min(config.Width, Math.Max(220, Game1.uiViewport.Width - 24));
        int height = 82 + config.Rows * 66;
        int x = Math.Clamp(config.X, 0, Math.Max(0, Game1.uiViewport.Width - width));
        int y = Math.Clamp(config.Y, 0, Math.Max(0, Game1.uiViewport.Height - height));
        panel = new Rectangle(x, y, width, height);
        var b = e.SpriteBatch;
        IClickableMenu.drawTextureBox(b, x, y, width, height, Color.White);
        Text(b, "FISH TO CATCH", new(x + 16, y + 14), .8f, Game1.textColor);
        previous = new Rectangle(x + width - 80, y + 8, 30, 32);
        next = new Rectangle(x + width - 43, y + 8, 30, 32);
        Text(b, "<", new(previous.X + 8, previous.Y + 4), .9f, Game1.textColor);
        Text(b, ">", new(next.X + 8, next.Y + 4), .9f, Game1.textColor);
        string subtitle = !Service.HasPublished && Service.Scanning ? "Checking current conditions..." : Service.Fallback ? "Magical fallback · no ordinary fish now" : $"Available now · {Service.Suggestions.Count} uncaught";
        Text(b, Fit(subtitle, width - 30, .63f), new(x + 16, y + 40), .63f, Color.SaddleBrown);
        pages.Value = Math.Clamp(pages.Value, 0, PageCount - 1);
        var shown = Service.Suggestions.Skip(pages.Value * config.Rows).Take(config.Rows).ToArray();
        for (int i = 0; i < shown.Length; i++)
        {
            Suggestion fish = SuggestionPresentation.ForLocation(shown[i], Game1.currentLocation.NameOrUniqueName, Game1.currentLocation.Name);
            int rowY = y + 63 + i * 66;
            var bounds = new Rectangle(x + 8, rowY, width - 16, 62);
            rows.Add((bounds, fish));
            if (bounds.Contains(Cursor())) b.Draw(Game1.staminaRect, bounds, Color.SaddleBrown * .12f);
            var item = ItemRegistry.GetDataOrErrorItem(FishingRules.Qualify(fish.Id));
            b.Draw(item.GetTexture(), new Rectangle(x + 16, rowY + 6, 40, 40), item.GetSourceRect(), Color.White);
            Text(b, Fit(fish.Name, width - 84, .82f), new(x + 67, rowY + 4), .82f, Game1.textColor);
            string location = SuggestionPresentation.LocationLine(fish);
            Text(b, Fit(location, width - 84, .64f), new(x + 67, rowY + 30), .64f, Color.SaddleBrown);
        }
        if (Service.HasPublished && shown.Length == 0)
        {
            Text(b, Fit("No eligible uncaught rod fish now.", width - 32, .68f), new(x + 16, y + 83), .68f, Game1.textColor);
            Text(b, Fit("Updates with time, weather and catches.", width - 32, .6f), new(x + 16, y + 112), .6f, Color.SaddleBrown);
        }
        string footer = Service.FailedLocations > 0 ? "Some locations unavailable · see SMAPI log"
            : $"{pages.Value + 1}/{PageCount} · " + (config.OnlyVisitedModLocations ? "Known areas · " : "") + "Click fish · Drag title";
        Text(b, Fit(footer, width - 30, .58f), new(x + 16, y + height - 24), .58f, Color.SaddleBrown);
        var hovered = rows.FirstOrDefault(p => p.Bounds.Contains(Cursor()));
        if (hovered.Fish != null)
            IClickableMenu.drawHoverText(b, Game1.parseText(SuggestionPresentation.Tooltip(hovered.Fish,
                Game1.currentLocation.NameOrUniqueName, Game1.currentLocation.Name), Game1.smallFont,
                Math.Max(180, Math.Min(560, Game1.uiViewport.Width - 64))), Game1.smallFont);
    }
    internal static void Text(SpriteBatch b, string text, Vector2 at, float scale, Color color) =>
        b.DrawString(Game1.smallFont, text, at, color, 0, Vector2.Zero, scale, SpriteEffects.None, 1);
    internal static string Fit(string text, int width, float scale)
    {
        if (Game1.smallFont.MeasureString(text).X * scale <= width) return text;
        while (text.Length > 0 && Game1.smallFont.MeasureString(text + "...").X * scale > width) text = text[..^1];
        return text + "...";
    }
}

internal sealed class FishDetailsMenu : IClickableMenu
{
    // Lookup Anything's public hover convention, like Birthday Planner.
    public Item? HoveredItem;
    private readonly Suggestion fish;
    private readonly bool fallback;
    private int index;
    private Rectangle icon;
    public FishDetailsMenu(Suggestion fish, bool fallback) : base(0, 0, 0, 0, true)
    { this.fish = fish; this.fallback = fallback; Layout(); }
    private void Layout()
    {
        width = Math.Min(760, Game1.uiViewport.Width - 48); height = Math.Min(540, Game1.uiViewport.Height - 48);
        xPositionOnScreen = (Game1.uiViewport.Width - width) / 2; yPositionOnScreen = (Game1.uiViewport.Height - height) / 2;
        initializeUpperRightCloseButton();
        icon = new Rectangle(xPositionOnScreen + 28, yPositionOnScreen + 30, 64, 64);
    }
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => Layout();
    public override void performHoverAction(int x, int y)
    { HoveredItem = icon.Contains(x,y) ? ItemRegistry.Create(FishingRules.Qualify(fish.Id)) : null; base.performHoverAction(x,y); }
    public override void receiveScrollWheelAction(int direction) => index = (index + (direction > 0 ? -1 : 1) + fish.Spots.Count) % fish.Spots.Count;
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (y >= yPositionOnScreen + height - 80 && y < yPositionOnScreen + height - 25)
            index = (index + (x < xPositionOnScreen + width / 2 ? -1 : 1) + fish.Spots.Count) % fish.Spots.Count;
        else base.receiveLeftClick(x,y,playSound);
    }
    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * .5f);
        drawTextureBox(b, xPositionOnScreen, yPositionOnScreen, width, height, Color.White);
        var item = ItemRegistry.GetDataOrErrorItem(FishingRules.Qualify(fish.Id));
        b.Draw(item.GetTexture(), icon, item.GetSourceRect(), Color.White);
        ModEntry.Text(b, ModEntry.Fit(fish.Name, width - 140, 1f), new(xPositionOnScreen + 112, yPositionOnScreen + 42), 1f, Game1.textColor);
        FishingSpot spot = fish.Spots[index];
        string text = spot.Label + "\n\n" + spot.Detail + "\n\n" + (fallback ? "Fallback: no ordinary uncaught fish available." : "Uncaught by your farmer · regular bait conditions.");
        string wrapped = Game1.parseText(text, Game1.smallFont, width - 64);
        ModEntry.Text(b, wrapped, new(xPositionOnScreen + 32, yPositionOnScreen + 122), .8f, Game1.textColor);
        ModEntry.Text(b, $"< Previous       Location {index + 1}/{fish.Spots.Count}       Next >",
            new(xPositionOnScreen + 36, yPositionOnScreen + height - 64), .8f, Game1.textColor);
        upperRightCloseButton?.draw(b); drawMouse(b);
    }
}
