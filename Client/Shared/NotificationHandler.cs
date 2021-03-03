namespace ThriveDevCenter.Client.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
        private readonly NavigationManager navManager;
        private readonly Dictionary<Type, List<(object, Func<SerializedNotification, Task>)>> handlers = new();

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

        public void Register<T>(INotificationHandler<T> handler) where T : SerializedNotification
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
                        handlerList = new List<(object, Func<SerializedNotification, Task>)>();
                        handlers.Add(notificationType, handlerList);
                    }

                    lock (handlerList)
                    {
                        handlerList.Add((handler, async s => await handler.Handle((T)s, default(CancellationToken))));
                    }
                }
            }
        }

        public void Unregister<T>(INotificationHandler<T> handler) where T : SerializedNotification
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
        }

        private async Task ForwardNotification(SerializedNotification notification)
        {
            var notificationType = notification.GetType();
            Console.WriteLine("Got notification: " + notificationType);

            try
            {
                List<(object, Func<SerializedNotification, Task>)> filtered;

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
            catch (Exception e)
            {
                // TODO: is this needed? one tutorial said that signalr has bad error reporting by default
                await Console.Error.WriteLineAsync(e + " " + e.StackTrace);

                throw;
            }
        }

        public async Task StartConnection()
        {
            hubConnection = new HubConnectionBuilder()
                .WithUrl(navManager.ToAbsoluteUri(
                    $"/notifications?majorVersion={AppVersion.Major}&minorVersion={AppVersion.Minor}"))
                .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new NotificationJsonConverter()))
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
                    // Log to the Console
                    logging.AddProvider(new JavaScriptConsoleLoggerProvider());

                    // This will set ALL logging to Debug level
                    logging.SetMinimumLevel(LogLevel.Debug);
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

            hubConnection.On<SerializedNotification>("ReceiveNotification", async (notification) =>
            {
                Console.WriteLine("Got a notification handler stuff");
                await ForwardNotification(notification);
            });

            hubConnection.Reconnecting += error =>
            {
                ConnectionLost = true;
                return Task.CompletedTask;
            };

            hubConnection.Reconnected += async newId =>
            {
                // Groups need to be re-joined after connection is recreated
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
            await hubConnection.DisposeAsync();
        }

        private async Task ApplyGroupMemberships()
        {
            Console.WriteLine("joining groups");
            await hubConnection.InvokeAsync("JoinGroup", NotificationGroups.LFSListUpdated);
        }

        private void ForceReload()
        {
            navManager.NavigateTo(navManager.Uri, true);
        }
    }
}
