using GLAS.App.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GLAS.App.Localization;

/// <summary>
/// Tiny i18n manager. Views bind to <c>{Binding [Key], Source={x:Static loc:Loc.Instance}}</c>;
/// changing Language raises a reset so every indexer binding re-reads.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private string _language = "es";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language
    {
        get => _language;
        set
        {
            if (_language != value && Table.ContainsKey(value))
            {
                _language = value;
                OnPropertyChanged();
            }
        }
    }

    public string this[string key] =>
        Table.TryGetValue(key, out string? value) ? value : key;

    private Dictionary<string, string> Table => _language switch
    {
        "en" => En,
        _ => Es,
    };

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void OnLanguageChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    private static readonly Dictionary<string, string> Es = new()
    {
        ["App.Title"] = "GLAS - Game Launcher Account Switcher",
        ["App.Version"] = "v" + AppInfo.Version,
        ["Toolbar.AddAccount"] = "Añadir cuenta",
        ["Toolbar.SaveCurrent"] = "Guardar sesión actual",
        ["Toolbar.Export"] = "Exportar",
        ["Toolbar.Import"] = "Importar",
        ["Toolbar.Settings"] = "Ajustes",
        ["Toolbar.Refresh"] = "Actualizar",
        ["Account.Switch"] = "Cambiar",
        ["Account.Rename"] = "Renombrar",
        ["Account.Delete"] = "Eliminar",
        ["Account.Active"] = "Activa",
        ["Account.Created"] = "Guardada el {0}",
        ["Accounts.Empty.Title"] = "Aún no hay cuentas",
        ["Accounts.Empty.Body"] = "Guarda la sesión actual o añade una cuenta nueva con el botón de arriba.",
        ["Accounts.NotInstalled"] = "{0} no está instalado en este equipo (o no se ha encontrado).",
        ["Accounts.NotInstalled.Title"] = "No instalado",
        ["Account.Switching"] = "Cambiando a {0}…",
        ["Account.SwitchDone"] = "Listo: {0}",
        ["Account.SwitchFailed"] = "Error al cambiar de cuenta: {0}",
        ["Capture.Saved"] = "Sesión guardada: {0}",
        ["Capture.Failed"] = "No se pudo guardar la sesión: {0}",
        ["Confirm.DeleteTitle"] = "Eliminar cuenta",
        ["Confirm.DeleteBody"] = "¿Eliminar la cuenta guardada \"{0}\"? Esta acción no cierra la sesión en el launcher ni borra la cuenta real, solo su copia en GLAS.",
        ["AddAccount.Title"] = "Añadir cuenta",
        ["AddAccount.ForPlatform"] = "Añadir cuenta de {0}",
        ["AddAccount.Step1"] = "Elige la plataforma y prepara el launcher. GLAS cerrará la sesión actual y abrirá la ventana de inicio de sesión.",
        ["AddAccount.Prepare"] = "Preparar launcher",
        ["AddAccount.Step2"] = "Ahora inicia sesión en el launcher con la cuenta NUEVA. Cuando termines, vuelve aquí, ponle un nombre y guárdala.",
        ["AddAccount.NameLabel"] = "Nombre para la cuenta",
        ["AddAccount.NamePlaceholder"] = "p. ej. Cuenta secundaria",
        ["AddAccount.Save"] = "Guardar cuenta",
        ["AddAccount.LoggedIn"] = "Ya inicié sesión",
        ["AddAccount.NotInstalledSuffix"] = " — no instalado",
        ["AddAccount.Cancel"] = "Cancelar",
        ["AddAccount.ErrorTitle"] = "No se pudo guardar la cuenta",
        ["Settings.Title"] = "Ajustes",
        ["Settings.Language"] = "Idioma",
        ["Settings.StartMinimized"] = "Iniciar minimizado en la bandeja",
        ["Settings.CloseToTray"] = "Cerrar a la bandeja (no salir)",
        ["Settings.AutoStart"] = "Arrancar con Windows",
        ["Settings.SteamArgs"] = "Argumentos de arranque de Steam",
        ["Settings.Protect"] = "Proteger snapshots con contraseña (AES-256)",
        ["Settings.ProtectHint"] = "Los archivos de sesión se guardan cifrados. Se pedirá la contraseña al abrir GLAS.",
        ["Settings.LauncherPaths"] = "Rutas de instalación",
        ["Settings.PathOverrideHint"] = "Ruta personalizada del launcher (vacío = detección automática)",
        ["Settings.Detected"] = "Detectado: {0}",
        ["Settings.NotDetected"] = "No detectado en este equipo",
        ["Settings.Password.Title"] = "Contraseña de protección",
        ["Settings.Password.New"] = "Nueva contraseña",
        ["Settings.Password.Confirm"] = "Confirmar contraseña",
        ["Settings.Password.Accept"] = "Aceptar",
        ["Settings.Password.Mismatch"] = "Las contraseñas no coinciden.",
        ["Settings.Password.Unlock"] = "Introduce la contraseña para desbloquear las cuentas",
        ["Settings.Password.Skip"] = "Continuar sin desbloquear",
        ["Settings.Password.Wrong"] = "Contraseña incorrecta.",
        ["Tray.Show"] = "Mostrar GLAS",
        ["Tray.SwitchTo"] = "Cambiar a…",
        ["Tray.Exit"] = "Salir",
        ["Common.Close"] = "Cerrar",
        ["Common.Error"] = "Error",
        ["Export.Title"] = "Exportar cuentas",
        ["Export.Done"] = "Cuentas exportadas a: {0}",
        ["Import.Title"] = "Importar cuentas",
        ["Import.Done"] = "Se añadieron {0} cuentas.",
        ["Import.DoneSkipped"] = "Se añadieron {0} cuentas. {1} ya existían y no se tocaron.",
        ["Import.PasswordNeeded"] = "El paquete está protegido. Introduce su contraseña.",
        ["Update.Title"] = "Actualizaciones",
        ["Update.Available"] = "Hay una nueva versión disponible: {0}. ¿Quieres abrir la página de descargas?",
        ["Update.UpToDate"] = "Ya tienes la última versión ({0}).",
        ["Settings.CheckUpdates"] = "Buscar actualizaciones",
        ["Update.Unavailable"] = "No se pudo comprobar si hay actualizaciones (sin conexión o aún sin publicaciones).",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        ["App.Title"] = "GLAS - Game Launcher Account Switcher",
        ["App.Version"] = "v" + AppInfo.Version,
        ["Toolbar.AddAccount"] = "Add account",
        ["Toolbar.SaveCurrent"] = "Save current session",
        ["Toolbar.Export"] = "Export",
        ["Toolbar.Import"] = "Import",
        ["Toolbar.Settings"] = "Settings",
        ["Toolbar.Refresh"] = "Refresh",
        ["Account.Switch"] = "Switch",
        ["Account.Rename"] = "Rename",
        ["Account.Delete"] = "Delete",
        ["Account.Active"] = "Active",
        ["Account.Created"] = "Saved on {0}",
        ["Accounts.Empty.Title"] = "No accounts yet",
        ["Accounts.Empty.Body"] = "Save the current session or add a new account with the button above.",
        ["Accounts.NotInstalled"] = "{0} is not installed on this computer (or could not be found).",
        ["Accounts.NotInstalled.Title"] = "Not installed",
        ["Account.Switching"] = "Switching to {0}…",
        ["Account.SwitchDone"] = "Done: {0}",
        ["Account.SwitchFailed"] = "Could not switch account: {0}",
        ["Capture.Saved"] = "Session saved: {0}",
        ["Capture.Failed"] = "Could not save the session: {0}",
        ["Confirm.DeleteTitle"] = "Delete account",
        ["Confirm.DeleteBody"] = "Delete the saved account \"{0}\"? This does not log the launcher out nor delete the real account, only GLAS's copy.",
        ["AddAccount.Title"] = "Add account",
        ["AddAccount.ForPlatform"] = "Add {0} account",
        ["AddAccount.Step1"] = "Pick the platform and prepare the launcher. GLAS will end the current session and open the login window.",
        ["AddAccount.Prepare"] = "Prepare launcher",
        ["AddAccount.Step2"] = "Now sign into the launcher with the NEW account. When done, come back here, give it a name and save it.",
        ["AddAccount.NameLabel"] = "Account name",
        ["AddAccount.NamePlaceholder"] = "e.g. Secondary account",
        ["AddAccount.Save"] = "Save account",
        ["AddAccount.LoggedIn"] = "I have signed in",
        ["AddAccount.NotInstalledSuffix"] = " — not installed",
        ["AddAccount.Cancel"] = "Cancel",
        ["AddAccount.ErrorTitle"] = "Could not save the account",
        ["Settings.Title"] = "Settings",
        ["Settings.Language"] = "Language",
        ["Settings.StartMinimized"] = "Start minimized to tray",
        ["Settings.CloseToTray"] = "Close to tray (don't exit)",
        ["Settings.AutoStart"] = "Start with Windows",
        ["Settings.SteamArgs"] = "Steam launch arguments",
        ["Settings.Protect"] = "Protect snapshots with a password (AES-256)",
        ["Settings.ProtectHint"] = "Session files are stored encrypted. The password is requested when GLAS opens.",
        ["Settings.LauncherPaths"] = "Installation paths",
        ["Settings.PathOverrideHint"] = "Custom launcher path (empty = auto-detect)",
        ["Settings.Detected"] = "Detected: {0}",
        ["Settings.NotDetected"] = "Not detected on this computer",
        ["Settings.Password.Title"] = "Protection password",
        ["Settings.Password.New"] = "New password",
        ["Settings.Password.Confirm"] = "Confirm password",
        ["Settings.Password.Accept"] = "Accept",
        ["Settings.Password.Mismatch"] = "Passwords do not match.",
        ["Settings.Password.Unlock"] = "Enter the password to unlock your accounts",
        ["Settings.Password.Skip"] = "Continue without unlocking",
        ["Settings.Password.Wrong"] = "Wrong password.",
        ["Tray.Show"] = "Show GLAS",
        ["Tray.SwitchTo"] = "Switch to…",
        ["Tray.Exit"] = "Exit",
        ["Common.Close"] = "Close",
        ["Common.Error"] = "Error",
        ["Export.Title"] = "Export accounts",
        ["Export.Done"] = "Accounts exported to: {0}",
        ["Import.Title"] = "Import accounts",
        ["Import.Done"] = "{0} accounts were added.",
        ["Import.DoneSkipped"] = "{0} accounts were added. {1} already existed and were left untouched.",
        ["Import.PasswordNeeded"] = "This package is protected. Enter its password.",
        ["Update.Title"] = "Updates",
        ["Update.Available"] = "A new version is available: {0}. Open the download page?",
        ["Update.UpToDate"] = "You are on the latest version ({0}).",
        ["Settings.CheckUpdates"] = "Check for updates",
        ["Update.Unavailable"] = "Could not check for updates (offline or no releases yet).",
    };
}
