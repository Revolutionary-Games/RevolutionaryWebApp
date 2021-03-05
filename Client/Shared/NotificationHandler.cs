namespace ThriveDevCenter.Client.Shared
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
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Notifications;

    public class NotificationHandler : IAsyncDisposable
    {
        private const bool FullMessageLogging = false;

        private readonly NavigationManager navManager;
        private readonly Dictionary<Type, List<(IGroupListener, Func<SerializedNotification, Task>)>> handlers = new();

        private readonly NotificationJsonConverter converter = new NotificationJsonConverter();

        private readonly HashSet<string> currentlyJoinedGroups = new();
        private HubConnection hubConnection;

        private bool connectionLost = false;
        private bool permanentlyLost = false;

        public NotificationHandler(NavigationManager navManager)
        {
            this.navManager = navManager;
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

        public async Task Register<T>(INotificationHandler<T> handler) where T : SerializedNotification
        {
            lock (handlers)
            {
                // Magic from https://remibou.github.io/Realtime-update-with-Blazor-WASM-SignalR-and-MediatR/
                var handlerInterfaces = handler
                    .GetType()
                    .GetInterfaces()
                    .Where(x =>
                        x.IsGenericType &&
                        x.GetGenericTypeDefinition() == typeof(INotificationHandler<>))
                    .ToList();
                foreach (var item in handlerInterfaces)
                {
                    var notificationType = item.GenericTypeArguments.First();
                    if (!handlers.TryGetValue(notificationType, out var handlerList))
                    {
                        handlerList = new List<(IGroupListener, Func<SerializedNotification, Task>)>();
                        handlers.Add(notificationType, handlerList);
                    }

                    lock (handlerList)
                    {
                        handlerList.Add((handler, async s => await handler.Handle((T)s, default(CancellationToken))));
                    }
                }
            }

            await ApplyGroupMemberships();
        }

        public async Task Unregister<T>(INotificationHandler<T> handler) where T : SerializedNotification
        {
            lock (handlers)
            {
                foreach (var item in handlers)
                {
                    lock (item.Value)
                    {
                        item.Value.RemoveAll(h => h.Item1.Equals(handler));
                    }
                }
            }

            await ApplyGroupMemberships();
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
                    $"/notifications?majorVersion={AppVersion.Major}&minorVersion={AppVersion.Minor}"))
                .AddJsonProtocol()
                .WithAutomaticReconnect(new TimeSpan[]
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
                ConnectionPermanentlyLost = true;
                await hubConnection.StopAsync();

                // Force reload as our session should be invalid now so we need to reopen everything as non-authenticated user
                ForceReload();
            });

            hubConnection.On("ReceiveVersionMismatch", () =>
            {
                VersionMisMatch = true;
                OnVersionMismatch?.Invoke(this, EventArgs.Empty);
            });

            hubConnection.On<string>("ReceiveNotificationJSON", async (json) =>
            {
                if (FullMessageLogging)
                    Console.WriteLine("Got a notification handler message: " + json);

                try
                {
                    var notification = JsonSerializer.Deserialize<SerializedNotification>(json,
                        new JsonSerializerOptions() { Converters = { converter } });

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

            hubConnection.Reconnecting += error =>
            {
                ConnectionLost = true;
                return Task.CompletedTask;
            };

            hubConnection.Reconnected += async newId =>
            {
                // Disallow reconnect in this case if we already gave up
                if (ConnectionPermanentlyLost)
                {
                    Console.WriteLine("Reconnected triggered while we already permanently gave up");
                    await hubConnection.StopAsync();
                    return;
                }

                // Groups need to be re-joined after connection is recreated
                currentlyJoinedGroups.Clear();
                await ApplyGroupMemberships();

                ConnectionLost = false;
            };

            hubConnection.Closed += error =>
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

            await ApplyGroupMemberships();
        }

        public async ValueTask DisposeAsync()
        {
            if (hubConnection == null)
                return;

            await hubConnection.DisposeAsync();
        }

        private async Task ApplyGroupMemberships()
        {
            // TODO: get current user status (once logging in is done)
            var userStatus = UserAccessLevel.Admin;

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

            await Task.WhenAll(groupTasks);

            // Currently joined groups should now have all the groups we have joined
        }

        // ReSharper restore ConditionIsAlwaysTrueOrFalse
#pragma warning restore 0162

        private void ForceReload()
        {
            navManager.NavigateTo(navManager.Uri, true);
        }
    }
}
