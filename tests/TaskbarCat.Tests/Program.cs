using TaskbarCat;

internal static class Program
{
    private static int passed;

    [STAThread]
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Standardname und Bewegungsbereich", DefaultsAreSafe),
            ("Enter bestätigt die Namenssuche", SearchEnterConfirms),
            ("Enter sendet eine Chatnachricht", MessageEnterConfirms),
            ("Umschalt+Enter erzeugt einen Zeilenumbruch", ShiftEnterDoesNotSend),
            ("Andere Tasten lösen keine Aktion aus", OtherKeysDoNothing),
            ("Einstellungen schließen nicht global per Enter oder Escape", SettingsHaveNoGlobalDialogKeys),
            ("Online- und Zuletzt-online-Anzeige", PresenceTextIsReadable),
            ("Produktionsdienst verwendet HTTPS", ProductionApiUsesHttps),
            ("Lauf-Sprites sind vollständig", WalkSpritesLoad),
            ("Sprung-Sprites sind vollständig", JumpSpritesLoad),
            ("Putz-Sprites sind vollständig", GroomSpritesLoad)
        };

        foreach (var test in tests)
        {
            try
            {
                test.Run();
                passed++;
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL  {test.Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"{passed}/{tests.Length} Tests erfolgreich.");
        return passed == tests.Length ? 0 : 1;
    }

    private static void DefaultsAreSafe()
    {
        var settings = new CatSettings();
        Equal("Sneaker", settings.Name);
        Equal(4, settings.LeftPercent);
        Equal(96, settings.RightPercent);
    }

    private static void SearchEnterConfirms()
    {
        int confirmations = 0;
        var key = new KeyEventArgs(Keys.Enter);
        SettingsKeyboard.HandleSearch(key, () => confirmations++);
        Equal(1, confirmations);
        True(key.SuppressKeyPress, "Enter muss unterdrückt werden.");
    }

    private static void MessageEnterConfirms()
    {
        int confirmations = 0;
        var key = new KeyEventArgs(Keys.Enter);
        SettingsKeyboard.HandleMessage(key, () => confirmations++);
        Equal(1, confirmations);
        True(key.SuppressKeyPress, "Enter muss unterdrückt werden.");
    }

    private static void ShiftEnterDoesNotSend()
    {
        int confirmations = 0;
        var key = new KeyEventArgs(Keys.Shift | Keys.Enter);
        SettingsKeyboard.HandleMessage(key, () => confirmations++);
        Equal(0, confirmations);
        True(!key.SuppressKeyPress, "Umschalt+Enter muss beim Textfeld ankommen.");
    }

    private static void OtherKeysDoNothing()
    {
        int confirmations = 0;
        var key = new KeyEventArgs(Keys.A);
        SettingsKeyboard.HandleSearch(key, () => confirmations++);
        SettingsKeyboard.HandleMessage(key, () => confirmations++);
        Equal(0, confirmations);
        True(!key.SuppressKeyPress, "Normale Eingaben dürfen nicht unterdrückt werden.");
    }

    private static void SettingsHaveNoGlobalDialogKeys()
    {
        var settings = new CatSettings();
        using var messaging = new MessagingService(settings, () => { });
        using var form = new SettingsForm(settings, messaging);
        True(form.AcceptButton is null, "AcceptButton darf nicht gesetzt sein.");
        True(form.CancelButton is null, "CancelButton darf nicht gesetzt sein.");
        var buttonLabels = Descendants(form).OfType<Button>().Select(x => x.Text).ToHashSet();
        True(buttonLabels.Contains("Speichern"), "Speichern-Schaltfläche fehlt.");
        True(buttonLabels.Contains("Abbrechen"), "Abbrechen-Schaltfläche fehlt.");
    }

    private static void PresenceTextIsReadable()
    {
        Equal("● online", CatContact.PresenceText(true, 0));
        Equal("noch nie online", CatContact.PresenceText(false, 0));
        string recent = CatContact.PresenceText(false, DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds());
        True(recent.Contains("Min."), "Ein kürzlich abgemeldeter Kontakt braucht eine Minutenangabe.");
    }

    private static void ProductionApiUsesHttps()
    {
        True(Uri.TryCreate(MessagingService.ApiBase, UriKind.Absolute, out var uri), "API-Adresse ist ungültig.");
        Equal(Uri.UriSchemeHttps, uri!.Scheme);
        True(!uri.IsLoopback, "Produktionsbuild darf nicht auf localhost zeigen.");
    }

    private static void WalkSpritesLoad() => VerifySprites(
        ImageSpriteLoader.LoadGrid("TaskbarCat.Assets.sneaker-walk.png", 8, 1, 0, 1, 2, 4, 5, 6), 6);

    private static void JumpSpritesLoad() => VerifySprites(
        ImageSpriteLoader.LoadGrid("TaskbarCat.Assets.sneaker-jump.png", 8, 1, 0, 1, 2, 3, 4, 5, 6, 7), 8);

    private static void GroomSpritesLoad() => VerifySprites(
        ImageSpriteLoader.LoadGrid("TaskbarCat.Assets.sneaker-groom.png", 8, 1, 0, 1, 2, 3, 4, 5, 6, 7), 8);

    private static void VerifySprites(Bitmap[] frames, int expectedCount)
    {
        try
        {
            Equal(expectedCount, frames.Length);
            foreach (var frame in frames)
            {
                Equal(84, frame.Width);
                Equal(80, frame.Height);
                bool visible = false;
                for (int y = 0; y < frame.Height && !visible; y += 4)
                    for (int x = 0; x < frame.Width; x += 4)
                        if (frame.GetPixel(x, y).A > 0) { visible = true; break; }
                True(visible, "Ein Sprite-Frame ist vollständig transparent.");
            }
        }
        finally
        {
            foreach (var frame in frames) frame.Dispose();
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Erwartet: {expected}; erhalten: {actual}");
    }
}
