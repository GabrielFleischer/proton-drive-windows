﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtonDrive.App.Notifications;
using ProtonDrive.App.Notifications.Offers;
using ProtonDrive.App.Onboarding;
using ProtonDrive.App.Services;
using ProtonDrive.App.Settings;
using ProtonDrive.App.Windows.Views.Offer;
using ProtonDrive.Shared;
using ProtonDrive.Shared.Logging;
using ProtonDrive.Shared.Repository;
using ProtonDrive.Shared.Threading;
using static ProtonDrive.App.Settings.NotificationSettings;

namespace ProtonDrive.App.Windows.Services;

internal sealed class OfferNotificationService : IStoppableService, IOnboardingStateAware, IOffersAware
{
    public const string NotificationGroupId = "Offer";
    public const string NotificationId = "Offer";
    public const string GetDealActionName = "GetDeal";

    private readonly INotificationService _notificationService;
    private readonly IDialogService _dialogService;
    private readonly IApp _app;
    private readonly Func<OfferViewModel> _offerViewModelFactory;
    private readonly IRepository<NotificationSettings> _settingsRepository;
    private readonly IClock _clock;
    private readonly IScheduler _scheduler;
    private readonly ILogger<OfferNotificationService> _logger;

    private readonly CoalescingAction _stateChangeHandler;

    private OnboardingState _onboardingState = OnboardingState.Initial;
    private Offer? _offer;
    private bool _isStopping;

    public OfferNotificationService(
        INotificationService notificationService,
        IDialogService dialogService,
        IApp app,
        Func<OfferViewModel> offerViewModelFactory,
        IRepository<NotificationSettings> settingsRepository,
        IClock clock,
        [FromKeyedServices("Dispatcher")] IScheduler scheduler,
        ILogger<OfferNotificationService> logger)
    {
        _notificationService = notificationService;
        _dialogService = dialogService;
        _app = app;
        _offerViewModelFactory = offerViewModelFactory;
        _settingsRepository = settingsRepository;
        _clock = clock;
        _scheduler = scheduler;
        _logger = logger;

        _stateChangeHandler = logger.GetCoalescingActionWithExceptionsLoggingAndCancellationHandling(HandleExternalStateChangeAsync, nameof(OfferNotificationService));

        _notificationService.NotificationActivated += OnNotificationServiceNotificationActivated;
    }

    void IOnboardingStateAware.OnboardingStateChanged(OnboardingState value)
    {
        _onboardingState = value;

        ScheduleExternalStateChangeHandling();
    }

    void IOffersAware.OnActiveOfferChanged(Offer? offer)
    {
        _offer = offer;

        ScheduleExternalStateChangeHandling();
    }

    async Task IStoppableService.StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug($"{nameof(OfferNotificationService)} is stopping");

        _isStopping = true;
        _stateChangeHandler.Cancel();

        await WaitForCompletionAsync().ConfigureAwait(false);

        _logger.LogDebug($"{nameof(OfferNotificationService)} stopped");
    }

    internal Task WaitForCompletionAsync()
    {
        return _stateChangeHandler.WaitForCompletionAsync();
    }

    private static Notification GetNotificationInfo(Offer offer)
    {
        var message = Ensure.NotNull(offer.NotificationMessage, nameof(offer), nameof(offer.NotificationMessage));

        var notificationInfo = new Notification()
            .SetGroup(NotificationGroupId)
            .SetId(NotificationId)
            .SetHeaderText(message.HeaderText)
            .SetText(message.ContentText)
            .SetExpirationTime(offer.EndTimeUtc)
            .SuppressPopup();

        if (!string.IsNullOrEmpty(message.ButtonText))
        {
            notificationInfo = notificationInfo
                .AddButton(message.ButtonText, GetDealActionName);
        }

        if (!string.IsNullOrEmpty(message.LogoImageFilePath))
        {
            notificationInfo = notificationInfo
                .SetLogoImage(message.LogoImageFilePath);
        }

        return notificationInfo;
    }

    private void ScheduleExternalStateChangeHandling()
    {
        if (_isStopping)
        {
            return;
        }

        _stateChangeHandler.Run();
    }

    private Task HandleExternalStateChangeAsync(CancellationToken cancellationToken)
    {
        if (_onboardingState.Status is OnboardingStatus.Onboarding)
        {
            // Prevent showing offer notification during onboarding
            return Task.CompletedTask;
        }

        var offer = _offer;

        if (offer?.NotificationMessage is not null)
        {
            ShowNotificationIfNotYetShown(offer);
        }
        else
        {
            RemoveNotification();
        }

        return Task.CompletedTask;
    }

    private void ShowNotificationIfNotYetShown(Offer offer)
    {
        if (!HasNotificationBeenShown(offer.Id))
        {
            ShowNotification(offer);
        }
    }

    private void ShowNotification(Offer offer)
    {
        _notificationService.ShowNotification(GetNotificationInfo(offer));

        MarkNotificationAsShown(offer.Id);
    }

    private void RemoveNotification()
    {
        _notificationService.RemoveNotificationGroup(NotificationGroupId);
    }

    private void OnNotificationServiceNotificationActivated(object? sender, NotificationActivatedEventArgs e)
    {
        if (e.GroupId != NotificationGroupId)
        {
            return;
        }

        // Note. If the app was not running when the user clicked on the system toast notification,
        // the app is started, but active notification is not yet available. Therefore, the
        // offer dialog is not shown.
        Schedule(OpenOfferAsync);
    }

    private async Task OpenOfferAsync()
    {
        var offer = _offer;
        if (offer is null)
        {
            return;
        }

        var dialog = _offerViewModelFactory.Invoke();
        if (!dialog.SetDataItem(offer))
        {
            return;
        }

        await _app.ActivateAsync().ConfigureAwait(true);

        _dialogService.ShowDialog(dialog);
    }

    private bool HasNotificationBeenShown(string id)
    {
        return _settingsRepository.Get()?.ShownNotifications.Any(n => n.Id == id) == true;
    }

    private void MarkNotificationAsShown(string id)
    {
        var shownNotifications = (_settingsRepository.Get() ?? new NotificationSettings()).ShownNotifications;

        if (shownNotifications.Any(n => n.Id == id))
        {
            return;
        }

        var now = _clock.UtcNow;
        var roundedTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);

        var notification = new ShownNotification { Id = id, ShowingTimeUtc = roundedTime };

        var settings = new NotificationSettings
        {
            ShownNotifications = shownNotifications.Prepend(notification).ToList().AsReadOnly(),
        };

        _settingsRepository.Set(settings);
    }

    private void Schedule(Func<Task> action)
    {
        _scheduler.Schedule(action);
    }
}
