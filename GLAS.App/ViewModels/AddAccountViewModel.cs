using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLAS.App.Localization;
using GLAS.App.Services;
using GLAS.Core;
using GLAS.Core.Platforms;

namespace GLAS.App.ViewModels;

/// <summary>
/// Per-platform add-account flow: the dialog opens for the platform the user
/// is currently viewing, so there is no platform picker to confuse things.
/// </summary>
public partial class AddAccountViewModel : ObservableObject
{
    public enum Step
    {
        Choose,
        WaitingForLogin,
        Name,
    }

    private readonly Action _close;
    private readonly IPlatform _platform;

    public AddAccountViewModel(Action close, string platformId)
    {
        _close = close;
        _platform = AppServices.Instance.Platforms.Find(platformId)
            ?? throw new InvalidOperationException($"Unknown platform: {platformId}");

        (Glyph, Accent) = PlatformVisuals.For(platformId);
    }

    [ObservableProperty]
    private Step _currentStep = Step.Choose;

    public bool StepIsChoose => CurrentStep == Step.Choose;

    public bool StepIsWaiting => CurrentStep == Step.WaitingForLogin;

    public bool StepIsName => CurrentStep == Step.Name;

    public string PlatformName => _platform.Name;

    public string Glyph { get; }

    public string Accent { get; }

    public string Header => string.Format(Loc.Instance["AddAccount.ForPlatform"], _platform.Name);

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string _error = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public Loc Loc => Loc.Instance;

    partial void OnCurrentStepChanged(Step value)
    {
        OnPropertyChanged(nameof(StepIsChoose));
        OnPropertyChanged(nameof(StepIsWaiting));
        OnPropertyChanged(nameof(StepIsName));
    }

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    public bool HasError => Error.Length > 0;

    [RelayCommand]
    private async Task PrepareAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = string.Empty;
        try
        {
            await _platform.AddNewLoginAsync();
            CurrentStep = Step.WaitingForLogin;
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void DoneLoggingIn()
    {
        CurrentStep = Step.Name;
        AccountName = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        string name = AccountName.Trim();
        if (name.Length == 0)
        {
            Error = Loc.Instance["AddAccount.NameLabel"];
            return;
        }

        IsBusy = true;
        Error = string.Empty;
        try
        {
            await _platform.CaptureCurrentSessionAsync(name);
            _close();
        }
        catch (Exception exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _close();
}
