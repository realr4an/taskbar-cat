using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;

namespace TaskbarCat;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (AutoUpdater.TryHandleInstallerMode(args)) return;
        AutoUpdater.ScheduleCleanup(args);
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarCat");
        Directory.CreateDirectory(logDir);
        var log = Path.Combine(logDir, "startup.log");
        try
        {
            File.WriteAllText(log, $"{DateTime.Now:O} start{Environment.NewLine}");
            ApplicationConfiguration.Initialize();
            File.AppendAllText(log, "configuration ready\n");
            Application.Run(new CatContext(log));
        }
        catch (Exception ex)
        {
            File.AppendAllText(log, ex + Environment.NewLine);
            MessageBox.Show(ex.Message, "Taskbar Cat – Startfehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

internal sealed class CatSettings
{
    public int LeftPercent { get; set; } = 4;
    public int RightPercent { get; set; } = 96;
    public int MonitorIndex { get; set; }
    public string Name { get; set; } = "Sneaker";
    public string DeviceId { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string PrivateKeyProtected { get; set; } = "";
    public string DeviceTokenProtected { get; set; } = "";
    public List<CatContact> Contacts { get; set; } = new();
    public List<string> SeenMessageIds { get; set; } = new();
}

internal sealed class CatContext : ApplicationContext
{
    private readonly CatWindow cat;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu;
    private readonly MessagingService messaging;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskbarCat", "settings.json");
    private CatSettings settings;

    public CatContext(string startupLog)
    {
        settings = LoadSettings();
        File.AppendAllText(startupLog, "settings ready\n");
        cat = new CatWindow(settings);
        messaging = new MessagingService(settings, SaveSettings);
        messaging.MessageReceived += message => cat.ShowThought($"{message.SenderName}:\n{message.Text}");
        File.AppendAllText(startupLog, "cat window constructed\n");
        menu = new ContextMenuStrip();
        menu.Items.Add("Katzenmenü…", null, (_, _) => OpenSettings());
        menu.Items.Add("Katze pausieren", null, (_, _) => { cat.Paused = !cat.Paused; menu.Items[1].Text = cat.Paused ? "Katze weiterlaufen lassen" : "Katze pausieren"; });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => CloseApp());

        tray = new NotifyIcon
        {
            Text = TrayText(settings.Name),
            Icon = cat.CreateTrayIcon(),
            Visible = true
        };
        tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position); };
        cat.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) menu.Show(Cursor.Position); };
        tray.DoubleClick += (_, _) => OpenSettings();
        cat.Show();
        _ = messaging.StartAsync();
        File.AppendAllText(startupLog, "cat window shown\n");
        _ = AutoUpdater.CheckAndApplyAsync(cat, CloseApp);
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(settings, messaging);
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            settings = dialog.Value;
            cat.ApplySettings(settings);
            tray.Text = TrayText(settings.Name);
            SaveSettings();
        }
    }

    private static string TrayText(string name)
    {
        var text = string.IsNullOrWhiteSpace(name) ? "Taskbar Cat" : $"{name} – Taskbar Cat";
        return text[..Math.Min(text.Length, 63)];
    }

    private void CloseApp()
    {
        tray.Visible = false;
        ExitThread();
    }

    private CatSettings LoadSettings()
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<CatSettings>(File.ReadAllText(settingsPath)) ?? new();
            if (string.IsNullOrWhiteSpace(loaded.Name) || loaded.Name == "Minka") loaded.Name = "Sneaker";
            loaded.Contacts ??= new();
            loaded.SeenMessageIds ??= new();
            return loaded;
        }
        catch { return new(); }
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var temporary = settingsPath + ".new";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
        File.Move(temporary, settingsPath, true);
    }

    protected override void ExitThreadCore()
    {
        tray.Visible = false;
        tray.Dispose();
        messaging.Dispose();
        cat.Close();
        base.ExitThreadCore();
    }
}

internal sealed class CatWindow : Form
{
    private readonly Bitmap[] walkRight;
    private readonly Bitmap[] walkLeft;
    private readonly Bitmap[] sleepRight;
    private readonly Bitmap[] sleepLeft;
    private readonly Bitmap[] groomRight;
    private readonly Bitmap[] groomLeft;
    private readonly Bitmap[] jumpRight;
    private readonly Bitmap[] jumpLeft;
    private readonly Bitmap awakeRight;
    private readonly Bitmap awakeLeft;
    private readonly Bitmap sleepyAwakeRight;
    private readonly Bitmap sleepyAwakeLeft;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly Random random = new();
    private CatSettings settings;
    private int direction = 1, tick, jumpTicks, preWalkGroomTicks, settleGroomTicks, midGroomTicks, reactionTicks, clickHeartTicks;
    private int sleepBubbleTicks, sleepBubbleKind, nextSleepBubbleTick = 500;
    private float x;
    private float midRoutineProgress;
    private bool dragging, hovering, movedWhileDragging, journeyActive, jumpedThisJourney, didMidRoutine;
    private Point dragOffset, pressScreenPoint;
    private Bitmap? displayFrame;
    private ThoughtBubbleForm? thoughtBubble;
    public bool Paused;

    public CatWindow(CatSettings initial)
    {
        settings = initial;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        ClientSize = new Size(110, 120);
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);

        // Sneaker's custom image set: one coherent eight-frame sequence per action.
        // Frames 3 and 7 are airborne extension poses. Keep them out of the
        // ordinary gait; jumping is handled by its own occasional animation.
        walkRight = ImageSpriteLoader.LoadGrid("TaskbarCat.Assets.sneaker-walk.png", 8, 1, 0, 1, 2, 4, 5, 6);
        walkLeft = ImageSpriteLoader.Mirror(walkRight);
        groomRight = ImageSpriteLoader.LoadGrid("TaskbarCat.Assets.sneaker-groom.png", 8, 1, 0, 1, 2, 3, 4, 5, 6, 7);
        groomLeft = ImageSpriteLoader.Mirror(groomRight);
        // Reuse the final lie-down frame so grooming flows into sleep without
        // a change of character model, scale or silhouette.
        sleepRight = new[]
        {
            (Bitmap)groomRight[7].Clone(), (Bitmap)groomRight[7].Clone(),
            (Bitmap)groomRight[7].Clone(), (Bitmap)groomRight[7].Clone()
        };
        sleepLeft = ImageSpriteLoader.Mirror(sleepRight);
        sleepyAwakeRight = (Bitmap)groomRight[6].Clone();
        sleepyAwakeLeft = ImageSpriteLoader.Mirror(new[] { sleepyAwakeRight })[0];
        jumpRight = ImageSpriteLoader.LoadGrid("TaskbarCat.Assets.sneaker-jump.png", 8, 1, 0, 1, 2, 3, 4, 5, 6, 7);
        jumpLeft = ImageSpriteLoader.Mirror(jumpRight);
        awakeRight = (Bitmap)walkRight[0].Clone();
        awakeLeft = ImageSpriteLoader.Mirror(new[] { awakeRight })[0];
        displayFrame = sleepRight[0];

        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                movedWhileDragging = false;
                dragOffset = e.Location;
                pressScreenPoint = PointToScreen(e.Location);
                Capture = true;
            }
        };
        MouseMove += (_, e) =>
        {
            hovering = true;
            if (dragging)
            {
                var now = PointToScreen(e.Location);
                if (Math.Abs(now.X - pressScreenPoint.X) > 5 || Math.Abs(now.Y - pressScreenPoint.Y) > 5) movedWhileDragging = true;
                if (movedWhileDragging) MoveDragged(e);
            }
            Invalidate();
        };
        MouseEnter += (_, _) => hovering = true;
        MouseLeave += (_, _) => { if (!dragging) hovering = false; };
        MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            bool wasTap = dragging && !movedWhileDragging;
            dragging = false;
            Capture = false;
            if (movedWhileDragging) DropAndContinue(); else SnapToRange();
            if (wasTap) HandleTap();
        };

        ApplySettings(settings);
        timer.Tick += (_, _) => Animate();
        timer.Start();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; } }

    public void ApplySettings(CatSettings value)
    {
        settings = value;
        var area = CurrentArea();
        if (x == 0)
        {
            bool sleepLeft = random.Next(2) == 0;
            x = sleepLeft ? MinX(area) : MaxX(area);
            direction = sleepLeft ? 1 : -1;
        }
        else
        {
            x = Math.Clamp(x, MinX(area), MaxX(area));
            if (!journeyActive)
            {
                bool nearerLeft = Math.Abs(x - MinX(area)) <= Math.Abs(x - MaxX(area));
                x = nearerLeft ? MinX(area) : MaxX(area);
                direction = nearerLeft ? 1 : -1;
            }
        }
        Location = new Point((int)x, area.Bottom - Height);
    }

    public void ShowThought(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => ShowThought(text)); return; }
        thoughtBubble?.Close();
        thoughtBubble = new ThoughtBubbleForm(this, text);
        thoughtBubble.Show();
    }

    private Rectangle CurrentArea()
    {
        var screens = Screen.AllScreens;
        return screens[Math.Clamp(settings.MonitorIndex, 0, screens.Length - 1)].WorkingArea;
    }
    private int MinX(Rectangle a) => a.Left + (a.Width * settings.LeftPercent / 100);
    private int MaxX(Rectangle a) => Math.Max(MinX(a), a.Left + (a.Width * settings.RightPercent / 100) - Width);

    private void StartJourney()
    {
        if (Paused || journeyActive) return;
        var area = CurrentArea();
        bool atLeft = Math.Abs(x - MinX(area)) <= Math.Abs(x - MaxX(area));
        direction = atLeft ? 1 : -1;
        journeyActive = true;
        sleepBubbleTicks = 0;
        jumpedThisJourney = false;
        didMidRoutine = false;
        midRoutineProgress = 0.25f + (float)random.NextDouble() * 0.5f;
        settleGroomTicks = 0;
        preWalkGroomTicks = 75;
        displayFrame = direction > 0 ? groomRight[0] : groomLeft[0];
        Invalidate();
    }

    private void HandleTap()
    {
        if (Paused) return;
        bool currentlyWalking = journeyActive && preWalkGroomTicks == 0 && midGroomTicks == 0 && jumpTicks == 0 && reactionTicks == 0;
        if (currentlyWalking)
        {
            // A running Sneaker answers a click with two small happy hops,
            // then continues the interrupted journey in the same direction.
            reactionTicks = 56;
            clickHeartTicks = 60;
            jumpTicks = 0;
            Invalidate();
            return;
        }
        StartJourney();
    }

    private void Animate()
    {
        var area = CurrentArea();
        TopMost = true;
        if (dragging) { displayFrame = direction > 0 ? awakeRight : awakeLeft; Invalidate(); return; }
        if (Paused) { displayFrame = direction > 0 ? sleepRight[0] : sleepLeft[0]; Invalidate(); return; }
        tick++;
        if (clickHeartTicks > 0) clickHeartTicks--;

        if (settleGroomTicks > 0)
        {
            var grooming = direction > 0 ? groomRight : groomLeft;
            int elapsed = 90 - settleGroomTicks;
            int frame = Math.Min(grooming.Length - 1, elapsed * grooming.Length / 90);
            displayFrame = grooming[frame];
            settleGroomTicks--;
            if (settleGroomTicks == 0) displayFrame = direction > 0 ? sleepRight[0] : sleepLeft[0];
            Invalidate();
            return;
        }

        if (!journeyActive && hovering)
        {
            sleepBubbleTicks = 0;
            // Hover only makes a sleepy Sneaker lift her head while she stays down.
            // Walking still requires an explicit click.
            displayFrame = direction > 0 ? sleepyAwakeRight : sleepyAwakeLeft;
            Invalidate();
            return;
        }

        if (preWalkGroomTicks > 0)
        {
            var grooming = direction > 0 ? groomRight : groomLeft;
            int elapsed = 75 - preWalkGroomTicks;
            // Only use the sitting-and-licking portion before departure.
            // The final two lie-down frames belong exclusively at journey end.
            int preWalkFrameCount = Math.Min(6, grooming.Length);
            int frame = Math.Min(preWalkFrameCount - 1, elapsed * preWalkFrameCount / 75);
            displayFrame = grooming[frame];
            preWalkGroomTicks--;
            Invalidate();
            return;
        }

        if (midGroomTicks > 0)
        {
            var grooming = direction > 0 ? groomRight : groomLeft;
            int elapsed = 104 - midGroomTicks;
            if (elapsed < 72)
            {
                int frame = Math.Min(5, elapsed * 6 / 72);
                displayFrame = grooming[frame];
            }
            else
            {
                // Stay seated for the short "Miau!" before continuing.
                displayFrame = grooming[0];
            }
            midGroomTicks--;
            Invalidate();
            return;
        }

        if (reactionTicks > 0)
        {
            int phase = 56 - reactionTicks;
            int localHopPhase = phase % 28;
            int lift = (int)Math.Round(Math.Sin(localHopPhase * Math.PI / 27.0) * 10);
            x = Math.Clamp(x + direction * 0.16f, MinX(area), MaxX(area));
            int jumpFrame = JumpFrameIndex(localHopPhase, 28, jumpRight.Length);
            displayFrame = direction > 0 ? jumpRight[jumpFrame] : jumpLeft[jumpFrame];
            Location = new Point((int)x, area.Bottom - Height - lift);
            reactionTicks--;
            if (reactionTicks == 0) Location = new Point((int)x, area.Bottom - Height);
            Invalidate();
            return;
        }

        if (!journeyActive)
        {
            var sleeping = direction > 0 ? sleepRight : sleepLeft;
            displayFrame = sleeping[(tick / 30) % sleeping.Length];
            if (sleepBubbleTicks > 0)
            {
                sleepBubbleTicks--;
            }
            else if (tick >= nextSleepBubbleTick)
            {
                sleepBubbleKind = random.Next(2);
                sleepBubbleTicks = 120;
                nextSleepBubbleTick = tick + random.Next(700, 1300);
            }
            Location = new Point((int)x, area.Bottom - Height);
            Invalidate();
            return;
        }

        if (jumpTicks > 0)
        {
            int phase = 42 - jumpTicks;
            int jumpFrame = JumpFrameIndex(phase, 42, jumpRight.Length);
            displayFrame = direction > 0 ? jumpRight[jumpFrame] : jumpLeft[jumpFrame];
            int lift = (int)Math.Round(Math.Sin(phase * Math.PI / 41.0) * 25);
            x = Math.Clamp(x + direction * 0.52f, MinX(area), MaxX(area));
            Location = new Point((int)x, area.Bottom - Height - lift);
            jumpTicks--;
            if (jumpTicks == 0) Location = new Point((int)x, area.Bottom - Height);
            Invalidate();
            return;
        }

        x += direction * 0.28f;
        int min = MinX(area), max = MaxX(area);
        float progress = max == min ? 1 : (x - min) / (max - min);
        float directionalProgress = direction > 0 ? progress : 1f - progress;
        if (!didMidRoutine && directionalProgress >= midRoutineProgress)
        {
            didMidRoutine = true;
            midGroomTicks = 104;
            displayFrame = direction > 0 ? groomRight[0] : groomLeft[0];
            Location = new Point((int)x, area.Bottom - Height);
            Invalidate();
            return;
        }
        if (!jumpedThisJourney && progress is > 0.38f and < 0.62f)
        {
            jumpedThisJourney = true;
            jumpTicks = 42;
        }
        if (x <= min || x >= max)
        {
            x = Math.Clamp(x, min, max);
            journeyActive = false;
            direction = x <= min ? 1 : -1;
            settleGroomTicks = 90;
            displayFrame = direction > 0 ? groomRight[0] : groomLeft[0];
        }
        else
        {
            // Smooth 60 Hz translation, with the artist's gait in its authored order.
            int walkIndex = (tick / 6) % walkRight.Length;
            displayFrame = direction > 0 ? walkRight[walkIndex] : walkLeft[walkIndex];
        }
        Location = new Point((int)x, area.Bottom - Height);
        Invalidate();
    }

    private void MoveDragged(MouseEventArgs e)
    {
        var p = PointToScreen(e.Location);
        var area = CurrentArea();
        x = Math.Clamp(p.X - dragOffset.X, MinX(area), MaxX(area));
        Location = new Point((int)x, Math.Min(p.Y - dragOffset.Y, area.Bottom - Height));
    }
    private void SnapToRange() { var a = CurrentArea(); x = Math.Clamp(Left, MinX(a), MaxX(a)); Location = new Point((int)x, a.Bottom - Height); }
    private void DropAndContinue()
    {
        var a = CurrentArea();
        int min = MinX(a), max = MaxX(a);
        x = Math.Clamp(Left, min, max);
        if (x <= min + 2) direction = 1;
        else if (x >= max - 2) direction = -1;
        journeyActive = true;
        preWalkGroomTicks = 0;
        settleGroomTicks = 0;
        reactionTicks = 0;
        jumpTicks = 0;
        jumpedThisJourney = false;
        didMidRoutine = false;
        midRoutineProgress = 0.25f + (float)random.NextDouble() * 0.5f;
        displayFrame = direction > 0 ? awakeRight : awakeLeft;
        Location = new Point((int)x, a.Bottom - Height);
    }

    private static int JumpFrameIndex(int phase, int duration, int frameCount)
    {
        // Rise through the poses and play them backwards on landing. This keeps
        // take-off and touchdown visually continuous instead of snapping.
        double normalized = Math.Clamp((double)phase / Math.Max(1, duration - 1), 0, 1);
        double triangle = normalized <= 0.5 ? normalized * 2 : (1 - normalized) * 2;
        return Math.Clamp((int)Math.Round(triangle * (frameCount - 1)), 0, frameCount - 1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        var img = displayFrame ?? sleepRight[0];
        int px = (Width - img.Width) / 2, py = Height - img.Height;
        e.Graphics.DrawImageUnscaled(img, px, py);
        if (!journeyActive && !hovering && sleepBubbleTicks > 0)
        {
            using var white = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            using var outline = new Pen(Color.FromArgb(230, 45, 45, 45), 2);
            using var dark = new SolidBrush(Color.FromArgb(255, 35, 35, 35));
            if (sleepBubbleKind == 0)
            {
                var bubble = new Rectangle(5, 4, 82, 28);
                using var font = new Font("Segoe UI", 9, FontStyle.Bold);
                e.Graphics.FillEllipse(white, bubble);
                e.Graphics.DrawEllipse(outline, bubble);
                e.Graphics.FillEllipse(white, 77, 29, 9, 9);
                e.Graphics.DrawEllipse(outline, 77, 29, 9, 9);
                e.Graphics.DrawString("Schnarch…", font, dark, 13, 9);
            }
            else
            {
                var cloud = new Rectangle(31, 2, 67, 32);
                using var emoji = new Font("Segoe UI Emoji", 15);
                e.Graphics.FillEllipse(white, cloud);
                e.Graphics.DrawEllipse(outline, cloud);
                e.Graphics.FillEllipse(white, 25, 31, 10, 10);
                e.Graphics.DrawEllipse(outline, 25, 31, 10, 10);
                e.Graphics.FillEllipse(white, 20, 43, 6, 6);
                e.Graphics.DrawEllipse(outline, 20, 43, 6, 6);
                e.Graphics.DrawString("🐟", emoji, dark, 51, 5);
            }
        }
        if (midGroomTicks is > 0 and <= 32)
        {
            var bubble = new Rectangle(Width - 63, 4, 58, 27);
            using var white = new SolidBrush(Color.White);
            using var outline = new Pen(Color.FromArgb(230, 35, 35, 35), 2);
            using var textBrush = new SolidBrush(Color.FromArgb(255, 25, 25, 25));
            using var font = new Font("Segoe UI", 10, FontStyle.Bold);
            e.Graphics.FillEllipse(white, bubble);
            e.Graphics.DrawEllipse(outline, bubble);
            var tail = new[] { new Point(Width - 28, 29), new Point(Width - 21, 38), new Point(Width - 17, 27) };
            e.Graphics.FillPolygon(white, tail);
            e.Graphics.DrawLines(outline, tail);
            var size = e.Graphics.MeasureString("Miau!", font);
            e.Graphics.DrawString("Miau!", font, textBrush, bubble.X + (bubble.Width - size.Width) / 2, bubble.Y + 4);
        }
        if ((hovering && !dragging) || clickHeartTicks > 0)
        {
            using var b = new SolidBrush(Color.FromArgb(235, 90, 225, 120));
            using var f = new Font("Segoe UI Emoji", 12);
            e.Graphics.DrawString("♥", f, b, Width - 25, 1);
        }
    }

    public Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(awakeRight, new Rectangle(1, 1, 30, 30));
        return (Icon)Icon.FromHandle(bmp.GetHicon()).Clone();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            thoughtBubble?.Close();
            foreach (var f in walkRight) f?.Dispose();
            foreach (var f in walkLeft) f?.Dispose();
            foreach (var f in sleepRight) f?.Dispose();
            foreach (var f in sleepLeft) f?.Dispose();
            foreach (var f in groomRight) f?.Dispose();
            foreach (var f in groomLeft) f?.Dispose();
            foreach (var f in jumpRight) f?.Dispose();
            foreach (var f in jumpLeft) f?.Dispose();
            awakeRight.Dispose();
            awakeLeft.Dispose();
            sleepyAwakeRight.Dispose();
            sleepyAwakeLeft.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class RangeForm : Form
{
    private readonly TrackBar left = new() { Minimum = 0, Maximum = 90, TickFrequency = 10, Width = 330 };
    private readonly TrackBar right = new() { Minimum = 10, Maximum = 100, TickFrequency = 10, Width = 330 };
    private readonly ComboBox monitor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
    private readonly TextBox catName = new() { Width = 330, MaxLength = 40 };
    private readonly Label summary = new() { AutoSize = true };
    public CatSettings Value => new() { LeftPercent = left.Value, RightPercent = right.Value, MonitorIndex = monitor.SelectedIndex, Name = string.IsNullOrWhiteSpace(catName.Text) ? "Sneaker" : catName.Text.Trim() };

    public RangeForm(CatSettings current)
    {
        Text = "Taskbar Cat – Einstellungen";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(390, 405);
        Padding = new Padding(20);
        foreach (var s in Screen.AllScreens) monitor.Items.Add($"{s.DeviceName} ({s.Bounds.Width} × {s.Bounds.Height})");
        monitor.SelectedIndex = Math.Clamp(current.MonitorIndex, 0, monitor.Items.Count - 1);
        catName.Text = string.IsNullOrWhiteSpace(current.Name) ? "Sneaker" : current.Name;
        left.Value = Math.Clamp(current.LeftPercent, 0, 90);
        right.Value = Math.Clamp(current.RightPercent, 10, 100);

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(new Label { Text = "Name der Katze", AutoSize = true }); layout.Controls.Add(catName);
        layout.Controls.Add(new Label { Text = "Bildschirm für die Katze", AutoSize = true, Margin = new Padding(3, 14, 3, 0) }); layout.Controls.Add(monitor);
        layout.Controls.Add(new Label { Text = "Linke Grenze", AutoSize = true, Margin = new Padding(3, 14, 3, 0) }); layout.Controls.Add(left);
        layout.Controls.Add(new Label { Text = "Rechte Grenze", AutoSize = true }); layout.Controls.Add(right);
        layout.Controls.Add(summary);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(170, 15, 0, 0) };
        var ok = new Button { Text = "Übernehmen", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); layout.Controls.Add(buttons); Controls.Add(layout);
        AcceptButton = ok; CancelButton = cancel;
        left.ValueChanged += (_, _) => ValidateRange(true); right.ValueChanged += (_, _) => ValidateRange(false); UpdateSummary();
    }
    private void ValidateRange(bool changedLeft)
    {
        if (right.Value - left.Value < 10) { if (changedLeft) right.Value = Math.Min(100, left.Value + 10); else left.Value = Math.Max(0, right.Value - 10); }
        UpdateSummary();
    }
    private void UpdateSummary() => summary.Text = $"Die Katze nutzt {left.Value}% bis {right.Value}% der unteren Bildschirmkante.";
}

internal sealed class SettingsForm : Form
{
    private readonly CatSettings current;
    private readonly MessagingService messaging;
    private readonly TextBox catName = new() { Width = 490, MaxLength = 40 };
    private readonly ComboBox monitor = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 490 };
    private readonly TrackBar left = new() { Minimum = 0, Maximum = 90, TickFrequency = 10, Width = 490 };
    private readonly TrackBar right = new() { Minimum = 10, Maximum = 100, TickFrequency = 10, Width = 490 };
    private readonly TextBox ownCode = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox friendCode = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly ListBox contacts = new();
    private readonly TextBox message = new() { Multiline = true, MaxLength = 500, ScrollBars = ScrollBars.Vertical };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.FromArgb(63, 94, 69), MaximumSize = new Size(350, 38) };
    public CatSettings Value => current;

    public SettingsForm(CatSettings current, MessagingService messaging)
    {
        this.current = current; this.messaging = messaging;
        Text = $"{current.Name} · Katzenmenü";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(610, 570);
        BackColor = Color.FromArgb(246, 242, 234); Font = new Font("Segoe UI", 10);
        var header = new Label { Text = "🐾  Taskbar Cat", Font = new Font("Segoe UI", 18, FontStyle.Bold), ForeColor = Color.FromArgb(35, 52, 39), AutoSize = true, Location = new Point(22, 14) };
        var tabs = new TabControl { Location = new Point(18, 56), Size = new Size(574, 448) };
        var general = Page("Meine Katze"); var friends = Page("Freunde"); var chat = Page("Nachricht");
        tabs.TabPages.AddRange(new[] { general, friends, chat });

        foreach (var s in Screen.AllScreens) monitor.Items.Add($"{s.DeviceName} ({s.Bounds.Width} × {s.Bounds.Height})");
        monitor.SelectedIndex = Math.Clamp(current.MonitorIndex, 0, monitor.Items.Count - 1);
        catName.Text = string.IsNullOrWhiteSpace(current.Name) ? "Sneaker" : current.Name;
        left.Value = Math.Clamp(current.LeftPercent, 0, 90); right.Value = Math.Clamp(current.RightPercent, 10, 100);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12) };
        stack.Controls.Add(LabelFor("Name der Katze")); stack.Controls.Add(catName);
        stack.Controls.Add(LabelFor("Bildschirm für die Katze", 13)); stack.Controls.Add(monitor);
        stack.Controls.Add(LabelFor("Linke Grenze", 13)); stack.Controls.Add(left);
        stack.Controls.Add(LabelFor("Rechte Grenze")); stack.Controls.Add(right);
        var rangeSummary = LabelFor(""); stack.Controls.Add(rangeSummary);
        void ValidateRange(bool changedLeft) { if (right.Value - left.Value < 10) { if (changedLeft) right.Value = Math.Min(100, left.Value + 10); else left.Value = Math.Max(0, right.Value - 10); } rangeSummary.Text = $"Bewegungsbereich: {left.Value}% bis {right.Value}%"; }
        left.ValueChanged += (_, _) => ValidateRange(true); right.ValueChanged += (_, _) => ValidateRange(false); ValidateRange(true);
        general.Controls.Add(stack);

        ownCode.SetBounds(15, 50, 520, 78); ownCode.Text = messaging.InviteCode;
        ownCode.Click += (_, _) => ownCode.SelectAll();
        Shown += async (_, _) => { await messaging.StartAsync(); ownCode.Text = messaging.InviteCode; };
        var copy = ButtonFor("Meinen Code kopieren", 15, 138, async () =>
        {
            try
            {
                status.Text = "Freundescode wird vorbereitet …";
                await messaging.EnsureReadyAsync();
                var code = messaging.InviteCode;
                if (!code.StartsWith("TC1.", StringComparison.Ordinal)) throw new InvalidOperationException("Der Freundescode ist noch nicht verfügbar.");
                ownCode.Text = code;
                // Windows can briefly lock the clipboard. This overload retries
                // instead of making the button appear to do nothing.
                Clipboard.SetDataObject(code, true, 10, 100);
                status.Text = "Freundescode wurde kopiert. ✓";
            }
            catch (Exception ex)
            {
                status.Text = "Kopieren fehlgeschlagen.";
                MessageBox.Show($"Der Freundescode konnte nicht kopiert werden.\n\n{ex.Message}", "Taskbar Cat", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        });
        friendCode.SetBounds(15, 225, 520, 72);
        var add = ButtonFor("Freund hinzufügen", 15, 307, async () => { try { var c = messaging.AddContact(friendCode.Text); contacts.Items.Add(c); friendCode.Clear(); status.Text = $"{c.Name} wurde hinzugefügt."; } catch (Exception ex) { MessageBox.Show(ex.Message, "Freundescode", MessageBoxButtons.OK, MessageBoxIcon.Information); } await Task.CompletedTask; });
        friends.Controls.AddRange(new Control[] { PositionedLabel("Dein persönlicher Freundescode", 15, 18), ownCode, copy, PositionedLabel("Code eines Freundes einfügen", 15, 193), friendCode, add });

        contacts.SetBounds(15, 46, 520, 115); contacts.Items.AddRange(current.Contacts.Cast<object>().ToArray());
        message.SetBounds(15, 208, 520, 110);
        var send = ButtonFor("Mit der Katze senden", 15, 334, async () => { if (contacts.SelectedItem is not CatContact c) { status.Text = "Bitte zuerst einen Freund auswählen."; return; } try { status.Text = "Wird verschlüsselt gesendet …"; await messaging.SendAsync(c, message.Text); message.Clear(); status.Text = "Nachricht ist unterwegs. 🐾"; } catch (Exception ex) { status.Text = ex.Message; } });
        chat.Controls.AddRange(new Control[] { PositionedLabel("An wen?", 15, 16), contacts, PositionedLabel("Nachricht (maximal 500 Zeichen)", 15, 177), message, send });

        var save = ButtonFor("Speichern", 397, 519, async () => { current.Name = string.IsNullOrWhiteSpace(catName.Text) ? "Sneaker" : catName.Text.Trim(); current.MonitorIndex = monitor.SelectedIndex; current.LeftPercent = left.Value; current.RightPercent = right.Value; DialogResult = DialogResult.OK; Close(); await Task.CompletedTask; });
        var cancel = ButtonFor("Abbrechen", 495, 519, async () => { DialogResult = DialogResult.Cancel; Close(); await Task.CompletedTask; });
        status.Location = new Point(22, 524); Controls.AddRange(new Control[] { header, tabs, status, save, cancel });
        AcceptButton = save; CancelButton = cancel;
    }
    private TabPage Page(string title) => new(title) { BackColor = BackColor, Padding = new Padding(12) };
    private static Label LabelFor(string text, int top = 3) => new() { Text = text, AutoSize = true, Margin = new Padding(3, top, 3, 2) };
    private static Label PositionedLabel(string text, int x, int y) => new() { Text = text, AutoSize = true, Location = new Point(x, y) };
    private static Button ButtonFor(string text, int x, int y, Func<Task> action)
    {
        var b = new Button { Text = text, Location = new Point(x, y), AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(220, 236, 219), ForeColor = Color.FromArgb(30, 57, 37), Padding = new Padding(6, 2, 6, 2) };
        b.FlatAppearance.BorderColor = Color.FromArgb(112, 145, 114); b.Click += async (_, _) => await action(); return b;
    }
}

internal sealed class ThoughtBubbleForm : Form
{
    private readonly Form cat; private readonly string text;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 }; private int remaining;
    public ThoughtBubbleForm(Form cat, string text)
    {
        this.cat = cat; this.text = text.Length > 560 ? text[..560] : text;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; BackColor = Color.Magenta; TransparencyKey = Color.Magenta; DoubleBuffered = true;
        using var font = new Font("Segoe UI", 10);
        var measured = TextRenderer.MeasureText(this.text, font, new Size(380, 1000), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
        ClientSize = new Size(Math.Clamp(measured.Width + 40, 150, 420), Math.Clamp(measured.Height + 48, 76, 430));
        remaining = Math.Clamp(180 + this.text.Length * 3, 200, 700);
        timer.Tick += (_, _) => { if (cat.IsDisposed || --remaining <= 0) Close(); else Reposition(); };
        Shown += (_, _) => { Reposition(); timer.Start(); };
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; return cp; } }
    private void Reposition() { var area = Screen.FromControl(cat).WorkingArea; Location = new Point(Math.Clamp(cat.Left + cat.Width / 2 - Width / 2, area.Left, area.Right - Width), Math.Max(area.Top, cat.Top - Height + 20)); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var body = new Rectangle(3, 3, Width - 7, Height - 25); int r = 18;
        using var path = new GraphicsPath(); path.AddArc(body.X, body.Y, r, r, 180, 90); path.AddArc(body.Right-r, body.Y, r, r, 270, 90); path.AddArc(body.Right-r, body.Bottom-r, r, r, 0, 90); path.AddArc(body.X, body.Bottom-r, r, r, 90, 90); path.CloseFigure();
        using var fill = new SolidBrush(Color.FromArgb(252, 255, 253)); using var border = new Pen(Color.FromArgb(45, 65, 49), 2);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        var tail = new[] { new Point(Width / 2 - 8, Height - 25), new Point(Width / 2 + 2, Height - 4), new Point(Width / 2 + 12, Height - 25) };
        e.Graphics.FillPolygon(fill, tail); e.Graphics.DrawLines(border, tail);
        using var font = new Font("Segoe UI", 10);
        TextRenderer.DrawText(e.Graphics, text, font, new Rectangle(18, 13, Width - 36, Height - 48), Color.FromArgb(28, 38, 31), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
