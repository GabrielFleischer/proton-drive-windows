﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProtonDrive.App.Account;
using ProtonDrive.App.Services;
using ProtonDrive.App.Settings.Remote;
using ProtonDrive.Client.Contracts;
using ProtonDrive.Client.Notifications;
using ProtonDrive.Client.Notifications.Contracts;
using ProtonDrive.Shared;
using ProtonDrive.Shared.Configuration;
using ProtonDrive.Shared.Extensions;
using ProtonDrive.Shared.Logging;
using ProtonDrive.Shared.Threading;
using ClientNotification = ProtonDrive.Client.Notifications.Contracts.Notification;

namespace ProtonDrive.App.Notifications.Offers;

internal sealed class OfferService : IStoppableService, IAccountStateAware, IUserStateAware, IRemoteSettingsAware, IDisposable
{
    private readonly AppConfig _appConfig;
    private readonly IClock _clock;
    private readonly INotificationClient _notificationClient;
    private readonly Lazy<IEnumerable<IOffersAware>> _offersAwareInstances;
    private readonly ILogger<OfferService> _logger;

    private readonly CoalescingAction _stateChangeHandler;
    private readonly ISchedulerTimer _timer;

    private AccountStatus _accountStatus;
    private RemoteSettings _settings = RemoteSettings.Default;
    private UserState _userState = UserState.Empty;
    private Offer? _activeOffer;
    private bool _isStopping;

    public OfferService(
        AppConfig appConfig,
        IScheduler scheduler,
        IClock clock,
        INotificationClient notificationClient,
        Lazy<IEnumerable<IOffersAware>> offersAwareInstances,
        ILogger<OfferService> logger)
    {
        _appConfig = appConfig;
        _clock = clock;
        _notificationClient = notificationClient;
        _offersAwareInstances = offersAwareInstances;
        _logger = logger;

        _stateChangeHandler = logger.GetCoalescingActionWithExceptionsLoggingAndCancellationHandling(HandleExternalStateChangeAsync, nameof(OfferService));

        _timer = scheduler.CreateTimer();
        _timer.Tick += (_, _) => _stateChangeHandler.Run();
        _timer.Interval = appConfig.OffersUpdateInterval.RandomizedWithDeviation(0.2);
    }

    void IAccountStateAware.OnAccountStateChanged(AccountState state)
    {
        var prevStatus = _accountStatus;
        _accountStatus = state.Status;

        bool currentSucceeded = state.Status is AccountStatus.Succeeded;
        bool previousSucceeded = prevStatus is AccountStatus.Succeeded;
        if (currentSucceeded != previousSucceeded)
        {
            ScheduleExternalStateChangeHandling(forceRestart: !currentSucceeded);
        }
    }

    void IUserStateAware.OnUserStateChanged(UserState state)
    {
        var prevState = _userState;
        _userState = state;

        if (HasChanged(state, prevState))
        {
            ScheduleExternalStateChangeHandling(forceRestart: false);
        }
    }

    void IRemoteSettingsAware.OnRemoteSettingsChanged(RemoteSettings settings)
    {
        var prevSettings = _settings;
        _settings = settings;

        if (prevSettings.HasInAppNotificationsEnabled != settings.HasInAppNotificationsEnabled)
        {
            ScheduleExternalStateChangeHandling(forceRestart: !settings.HasInAppNotificationsEnabled);
        }
    }

    async Task IStoppableService.StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug($"{nameof(OfferService)} is stopping");

        _isStopping = true;
        _stateChangeHandler.Cancel();
        _timer.Stop();

        await WaitForCompletionAsync().ConfigureAwait(false);

        _logger.LogDebug($"{nameof(OfferService)} stopped");
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    internal Task WaitForCompletionAsync()
    {
        return _stateChangeHandler.WaitForCompletionAsync();
    }

    private static bool HasChanged(UserState state, UserState prevState)
    {
        return
            state.IsEmpty != prevState.IsEmpty ||
            state.Type != prevState.Type ||
            state.SubscriptionPlanCode != prevState.SubscriptionPlanCode ||
            state.IsDelinquent != prevState.IsDelinquent ||
            state.CanBuySubscription != prevState.CanBuySubscription ||
            state.LatestSubscriptionCancellationTimeUtc != prevState.LatestSubscriptionCancellationTimeUtc ||
            state.SubscriptionPlanCouponCode != prevState.SubscriptionPlanCouponCode;
    }

    private static bool CanGetAnOffer(UserState userState)
    {
        return userState is
        {
            IsEmpty: false,
            IsDelinquent: false,
            Type: not UserType.Credentialless,
            SubscriptionPlanCode: not null,
            CanBuySubscription: true,
        };
    }

    private static bool IsEligibleForOffer(UserState userState, ClientNotification notification)
    {
        Ensure.NotNull(notification.Offer, nameof(notification), nameof(notification.Offer));

        // Subscription must not be cancelled after a first day of previous month
        var startDate = notification.StartTime.Date;
        var checkDate = startDate.AddDays(1 - startDate.Day).AddMonths(-1);

        if (userState.LatestSubscriptionCancellationTimeUtc is not null &&
            userState.LatestSubscriptionCancellationTimeUtc.Value.Date >= checkDate)
        {
            return false;
        }

        // The same coupon cannot be used a second time
        if (!string.IsNullOrEmpty(userState.SubscriptionPlanCouponCode) &&
            !string.IsNullOrEmpty(notification.Offer.CouponCode) &&
            notification.Offer.CouponCode.Equals(userState.SubscriptionPlanCouponCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private void ScheduleExternalStateChangeHandling(bool forceRestart)
    {
        if (_isStopping)
        {
            return;
        }

        if (forceRestart)
        {
            _stateChangeHandler.Cancel();
        }

        _stateChangeHandler.Run();
    }

    private Offer ToOffer(ClientNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification.Offer);

        var baseImagePath = _appConfig.AppFolderPath;

        return new Offer
        {
            Id = notification.Id,
            StartTimeUtc = notification.StartTime.UtcDateTime,
            EndTimeUtc = notification.EndTime.UtcDateTime,
            ClickUrl = notification.Offer.ClickUrl,
            ImageFilePath = Path.Combine(baseImagePath, notification.Offer.ImageUrl),
            Title = notification.Offer.Title,
            NotificationMessage = ToNotificationMessage(notification),
        };
    }

    private NotificationMessage? ToNotificationMessage(ClientNotification notification)
    {
        if (string.IsNullOrEmpty(notification.HeaderText) ||
            string.IsNullOrEmpty(notification.ContentText))
        {
            return null;
        }

        var baseImagePath = _appConfig.AppFolderPath;

        return new NotificationMessage
        {
            HeaderText = notification.HeaderText,
            ContentText = notification.ContentText,
            ButtonText = notification.ButtonText,
            LogoImageFilePath = string.IsNullOrEmpty(notification.LogoImageUrl) ? null : Path.Combine(baseImagePath, notification.LogoImageUrl),
        };
    }

    private async Task HandleExternalStateChangeAsync(CancellationToken cancellationToken)
    {
        var userState = _userState;

        if (_accountStatus is not AccountStatus.Succeeded ||
            !_settings.HasInAppNotificationsEnabled ||
            !CanGetAnOffer(userState))
        {
            _timer.Stop();
            SetActiveOffer(null);

            return;
        }

        _timer.Start();

        var offer = await GetActiveOfferAsync(userState, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        SetActiveOffer(offer);
    }

    private async Task<Offer?> GetActiveOfferAsync(UserState userState, CancellationToken cancellationToken)
    {
        var subscriptionPlanCode = userState.SubscriptionPlanCode;
        if (string.IsNullOrEmpty(subscriptionPlanCode))
        {
            return null;
        }

        var notifications = await _notificationClient.GetNotificationsAsync(subscriptionPlanCode, cancellationToken).ConfigureAwait(false);

        var now = _clock.UtcNow;

        return notifications
            .Where(n => n.Type is NotificationType.Offer &&
                now >= n.StartTime.UtcDateTime &&
                now <= (n.EndTime.UtcDateTime - _timer.Interval) &&
                IsEligibleForOffer(userState, n))
            .Select(ToOffer)
            .FirstOrDefault();
    }

    private void SetActiveOffer(Offer? offer)
    {
        if (_activeOffer == offer)
        {
            return;
        }

        _activeOffer = offer;

        LogOfferChange(offer);
        OnOfferChanged(offer);
    }

    private void LogOfferChange(Offer? offer)
    {
        if (offer is not null)
        {
            _logger.LogInformation(
                "Active offer changed to: StartTime={StartTime:yyyy-MM-ddTHH:mmZ}, EndTime={EndTime:yyyy-MM-ddTHH:mmZ}, ID=\"{Id}\"",
                offer.StartTimeUtc,
                offer.EndTimeUtc,
                offer.Id);
        }
        else
        {
            _logger.LogInformation("Active offer changed to: None");
        }
    }

    private void OnOfferChanged(Offer? offer)
    {
        foreach (var listener in _offersAwareInstances.Value)
        {
            listener.OnActiveOfferChanged(offer);
        }

        /* TODO: If there is no active offer after the app start, offer change should be notified.
         * TODO: If session ends, the offer change should be notified.
         * As offer notification service depends on offer change notifications, it would
         * make sense to notify the offer change (with no offer) once the offer service is
         * confident, that there is no offer available to the user. Like, when it receives
         * from other services all required data for this decision.
         */
    }
}
