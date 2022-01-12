namespace ThriveDevCenter.Client.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.SignalR.Client;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Shared;
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Converters;
    using ThriveDevCenter.Shared.Models;
    using ThriveDevCenter.Shared.Notifications;
    using Utilities;

    public class NotificationHandler : IAsyncDisposable
    {
        private const bool FullMessageLogging = false;

        private readonly NavigationManager navManager;
        private readonly CurrentUserInfo userInfoReceiver;
        private readonly ICSRFTokenReader csrfTokenReader;
        private readonly Dictionary<Type, List<(IGroupListener, Func<SerializedNotification, Task>)>> handlers = new();

        private readonly SemaphoreSlim groupJoinSemaphore = new SemaphoreSlim(1, 1);

        private readonly NotificationJsonConverter converter = new NotificationJsonConverter();

        private readonly HashSet<string> currentlyJoinedGroups = new();
        private HubConnection hubConnection;

        private bool connectionLost;
        private bool permanentlyLost;
        private bool userInfoRegistered;

        public NotificationHandler(NavigationManager navManager, CurrentUserInfo userInfoReceiver,
            ICSRFTokenReader csrfTokenReader)
        {
            this.navManager = navManager;
            this.userInfoReceiver = userInfoReceiver;
            this.csrfTokenReader = csrfTokenReader;
        }

        public delegate void ConnectionStatusEventHandler(object sender, bool connectionLost);

        public delegate void ConnectionRetryStatusEventHandler(object sender, bool connectionLostPermanently);

        public delegate void SiteNoticeEventHandler(object sender, (SiteNoticeType, string) info);

        public delegate void VersionMismatchEventHandler(object sender, EventArgs e);

        public event ConnectionStatusEventHandler OnConnectionStatusChanged;
        public event ConnectionRetryStatusEventHandler OnConnectionRetryModeChanged;
        public event SiteNoticeEventHandler OnSiteNoticeReceived;
        public event VersionMismatchEventHandler OnVersionMismatch;

        public SiteNoticeType CurrentNoticeType { get; private set; } = SiteNoticeType.Primary;
        public string CurrentNotice { get; private set; }

        public bool VersionMisMatch { get; private set; }

        /// <summary>
        ///   True when hub connection is currently active
        /// </summary>
        public bool IsConnected => hubConnection.State == HubConnectionState.Connected;

        /// <summary>
        ///   If true the connection to notifications is lost
        /// </summary>
        public bool ConnectionLost
        {
            get => connectionLost;
            private set
            {
                if (value == connectionLost)
                    return;

                connectionLost = value;
                OnConnectionStatusChanged?.Invoke(this, connectionLost);
            }
        }

        /// <summary>
        ///   If true the connection to notifications is permanently lost and won't be retried anymore
        /// </summary>
        public bool ConnectionPermanentlyLost
        {
            get => permanentlyLost;
            private set
            {
                if (value == permanentlyLost)
                    return;

                permanentlyLost = value;
                OnConnectionRetryModeChanged?.Invoke(this, permanentlyLost);
            }
        }

        /// <summary>
        ///   Register an object to receive notifications
        /// </summary>
        /// <param name="handler">The object that should receive the notifications</param>
        /// <typeparam name="T">The notification type to register for. NOTE: only one type is required</typeparam>
        /// <exception cref="ArgumentException">handler is null</exception>
        /// <exception cref="InvalidOperationException">handler's class is missing somethings</exception>
        /// <remarks>
        ///   <para>
        ///     A single call to this method is enough no matter how many interfaces handler implements.
        ///     This is because this loops all the implemented notification interfaces and registers each one
        ///     of them in a single call.
        ///   </para>
        /// </remarks>
        public async Task Register<T>(INotificationHandler<T> handler) where T : SerializedNotification
        {
            if (handler == null)
                throw new ArgumentException();

            bool hadAny = false;

            // Magic from https://remibou.github.io/Realtime-update-with-Blazor-WASM-SignalR-and-MediatR/
            var handlerInterfaces = handler
                .GetType()
                .GetInterfaces()
                .Where(x =>
                    x.IsGenericType &&
                    x.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
                .ToList();

            lock (handlers)
            {
                foreach (var item in handlerInterfaces)
                {
                    hadAny = true;

                    var notificationType = item.GenericTypeArguments.First();
                    if (!handlers.TryGetValue(notificationType, out var handlerList))
                    {
                        handlerList = new List<(IGroupListener, Func<SerializedNotification, Task>)>();
                        handlers.Add(notificationType, handlerList);
                    }

                    var method = handler.GetType().GetMethod(nameof(handler.Handle),
                        new[] { notificationType, typeof(CancellationToken) });

                    if (method == null)
                    {
                        var error = "Failed to find handler for specific notification in type";
                        Console.Error.WriteLine(error);
                        throw new InvalidOperationException(error);
                    }

                    lock (handlerList)
                    {
                        handlerList.Add((handler,
                            async s =>
                            {
                                // ReSharper disable once PossibleNullReferenceException
                                await (Task)method.Invoke(handler, new object[] { s, default(CancellationToken) });
                            }));
                    }
                }
            }

            if (!hadAny)
            {
                var error = "Object given to Register didn't implement any listener interfaces";
                await Console.Error.WriteLineAsync(error);
                throw new InvalidOperationException(error);
            }

            await ApplyGroupMemberships();
        }

        public async Task Unregister<T>(INotificationHandler<T> handler) where T : SerializedNotification
        {
            bool removed = false;

            lock (handlers)
            {
                foreach (var item in handlers)
                {
                    lock (item.Value)
                    {
                        if (item.Value.RemoveAll(h => h.Item1.Equals(handler)) > 0)
                            removed = true;
                    }
                }
            }

            if (removed)
                await ApplyGroupMemberships();
        }

        /// <summary>
        ///   Notifies that some listener has changed which groups it wants to listen to. Internally this triggers full
        ///   rechecking of groups.
        /// </summary>
        /// <remarks>
        ///   <para>
        ///      TODO: for performance reasons it would be nice to rate-limit this
        ///   </para>
        /// </remarks>
        public Task NotifyWantedGroupsChanged()
        {
            return ApplyGroupMemberships();
        }

        private async Task ForwardNotification(SerializedNotification notification)
        {
            var notificationType = notification.GetType();

            List<(IGroupListener, Func<SerializedNotification, Task>)> filtered;

            lock (handlers)
            {
                if (!handlers.TryGetValue(notificationType, out filtered))
                {
                    return;
                }
            }

            // TODO: is it bad that all of the tasks are started at once?
            var tasks = new List<Task>();

            lock (filtered)
            {
                // Bug: user info (especially redeeming codes) seems to cause an error in some .Next method
                // we need to queue or disallow (and require components to invoke) group changes in relation to
                // notifications
                foreach (var item in filtered)
                {
                    tasks.Add(item.Item2(notification));
                }
            }

            await Task.WhenAll(tasks);
        }

        // ReSharper disable ConditionIsAlwaysTrueOrFalse
#pragma warning disable 0162 // unreachable code caused by const

        public async Task StartConnection()
        {
            // TODO: look into enabling message pack protocol
            hubConnection = new HubConnectionBuilder()
                .WithUrl(navManager.ToAbsoluteUri(
                        $"/notifications?majorVersion={AppInfo.Major}&minorVersion={AppInfo.Minor}"),
                    options =>
                    {
                        // Apparently we have to leak this in the url as there is no other way to set this...
                        options.AccessTokenProvider = () => Task.FromResult(csrfTokenReader.Token);

                        // options.WebSocketConfiguration = socketOptions =>
                        // {
                        //     // Not supported in browser
                        //     socketOptions.SetRequestHeader("X-CSRF-Token", csrfTokenReader.Token);
                        // };

                        // options.Headers["X-CSRF-Token"] = csrfTokenReader.Token;
                    })
                .AddJsonProtocol(configure =>
                {
                    configure.PayloadSerializerOptions = HttpClientHelpers.GetOptionsWithSerializers();
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.FromSeconds(0),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(60),
                    TimeSpan.FromSeconds(90),
                    TimeSpan.FromSeconds(120),
                    TimeSpan.FromSeconds(500),
                }).ConfigureLogging(logging =>
                {
                    if (FullMessageLogging)
                    {
                        logging.AddProvider(new JavaScriptConsoleLoggerProvider());

                        logging.SetMinimumLevel(LogLevel.Debug);
                    }
                })
                .Build();

            hubConnection.On<SiteNoticeType, string>("ReceiveSiteNotice", (type, message) =>
            {
                CurrentNoticeType = type;
                CurrentNotice = message;

                OnSiteNoticeReceived?.Invoke(this, (type, message));
            });

            hubConnection.On("ReceiveSessionInvalidation", async () =>
            {
                await OnRequireStoppingConnection();

                // Force reload as our session should be invalid now so we need to reopen everything as non-authenticated user
                navManager.ForceReload();
            });

            hubConnection.On("ReceiveVersionMismatch", () =>
            {
                VersionMisMatch = true;
                OnVersionMismatch?.Invoke(this, EventArgs.Empty);
            });

            hubConnection.On<UserInfo>("ReceiveOwnUserInfo", (user) => { userInfoReceiver.OnReceivedOurInfo(user); });

            hubConnection.On<string>("ReceiveNotificationJSON", async (json) =>
            {
                if (FullMessageLogging)
                    Console.WriteLine("Got a notification handler message: " + json);

                try
                {
                    // TODO: unify this with HttpClientHelpers
                    var notification = JsonSerializer.Deserialize<SerializedNotification>(json,
                        new JsonSerializerOptions() { Converters = { converter, new TimeSpanConverter() } });

                    await ForwardNotification(notification);
                }
                catch (Exception e)
                {
                    // Error reporting here is needed as otherwise nothing is shown
                    await Console.Error.WriteLineAsync("Error processing received notification: " + e + " " +
                        e.StackTrace);

                    throw;
                }
            });

            hubConnection.Reconnecting += _ =>
            {
                ConnectionLost = true;
                return Task.CompletedTask;
            };

            hubConnection.Reconnected += async _ =>
            {
                // Disallow reconnect in this case if we already gave up
                if (ConnectionPermanentlyLost)
                {
                    Console.WriteLine("Reconnected triggered while we already permanently gave up");
                    await OnRequireStoppingConnection();
                    return;
                }

                // Groups need to be re-joined after connection is recreated
                currentlyJoinedGroups.Clear();
                await ApplyGroupMemberships();

                ConnectionLost = false;
            };

            hubConnection.Closed += _ =>
            {
                ConnectionLost = true;
                ConnectionPermanentlyLost = true;

                return Task.CompletedTask;
            };

            try
            {
                await hubConnection.StartAsync();
            }
            catch
            {
                ConnectionLost = true;
                ConnectionPermanentlyLost = true;
            }

            // ReSharper disable HeuristicUnreachableCode
            if (FullMessageLogging)
                Console.WriteLine("Performing post hub connection start operations");

            // ReSharper restore HeuristicUnreachableCode

            if (!userInfoRegistered)
            {
                // Due to difficult ordering, we register on behalf of the UserInfoReceiver
                await Register<UserUpdated>(userInfoReceiver);
                userInfoRegistered = true;
            }
            else
            {
                await ApplyGroupMemberships();
            }

            // Listen for future user info updates (as those affect which groups we want to be in)
            userInfoReceiver.OnUserInfoChanged += OnUserInfoChanged;
        }

        public async Task OnRequireStoppingConnection()
        {
            if (hubConnection == null || ConnectionPermanentlyLost)
                return;

            Console.WriteLine("Stopping hub connection as we have detected a condition that we need to do so");
            ConnectionPermanentlyLost = true;
            await hubConnection.StopAsync();
        }

        public async ValueTask DisposeAsync()
        {
            userInfoReceiver.OnUserInfoChanged -= OnUserInfoChanged;

            if (hubConnection == null)
                return;

            await hubConnection.DisposeAsync();
        }

        private async void OnUserInfoChanged(object sender, UserInfo info)
        {
            if (FullMessageLogging)
                Console.WriteLine("Applying groups because we got user info change notification");

            await ApplyGroupMemberships();
        }

        private async Task ApplyGroupMemberships()
        {
            // Can't run this yet if we don't have user info. We'll receive a callback when user info is ready
            if (!userInfoReceiver.InfoReady)
            {
                if (FullMessageLogging)
                    Console.WriteLine("User info not ready yet, can't check groups");
                return;
            }

            // We use a semaphore here to ensure only one thread applies groups at once
            if (!await groupJoinSemaphore.WaitAsync(TimeSpan.FromMinutes(1)))
            {
                await Console.Error.WriteLineAsync(
                    "Failed to get group join semaphore after one minute, can't join groups");
                return;
            }

            try
            {
                await PerformGroupApply();
            }
            finally
            {
                groupJoinSemaphore.Release();
            }
        }

        private async Task PerformGroupApply()
        {
            var userStatus = userInfoReceiver.AccessLevel;

            var wantedGroups = new HashSet<string>();

            lock (handlers)
            {
                foreach (var entry in handlers)
                {
                    lock (entry.Value)
                    {
                        foreach (var handler in entry.Value)
                        {
                            handler.Item1.GetWantedListenedGroups(userStatus, wantedGroups);
                        }
                    }
                }
            }

            if (FullMessageLogging)
                Console.WriteLine($"Wanted groups: {string.Join(' ', wantedGroups)}");

            var groupsToLeave = currentlyJoinedGroups.Except(wantedGroups).ToList();
            var groupsToJoin = wantedGroups.Except(currentlyJoinedGroups).ToList();

            // All the joins and leaves are run in parallel as they should not be able to have overlapping items
            var groupTasks = new List<Task>();

            foreach (var group in groupsToLeave)
            {
                if (FullMessageLogging)
                    Console.WriteLine("Leaving group: " + group);

                groupTasks.Add(hubConnection.InvokeAsync("LeaveGroup", group));
                currentlyJoinedGroups.Remove(group);
            }

            foreach (var group in groupsToJoin)
            {
                if (FullMessageLogging)
                    Console.WriteLine("Joining group: " + group);

                groupTasks.Add(hubConnection.InvokeAsync("JoinGroup", group));
                currentlyJoinedGroups.Add(group);
            }

            try
            {
                await Task.WhenAll(groupTasks);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error joining some notification group: {e}");

                // Let's send this info as a site notice to not require a new component to show this, and the user can
                // dismiss that if they want to
                OnSiteNoticeReceived?.Invoke(this,
                    (SiteNoticeType.Warning,
                        "Error joining a notification message group. You may not receive realtime data updates."));
            }

            // Currently joined groups should now have all the groups we have joined
        }

        // ReSharper restore ConditionIsAlwaysTrueOrFalse
#pragma warning restore 0162
    }
}
