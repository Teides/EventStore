// Copyright (c) 2012, Event Store LLP
// All rights reserved.
//  
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//  
// Redistributions of source code must retain the above copyright notice,
// this list of conditions and the following disclaimer.
// Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in the
// documentation and/or other materials provided with the distribution.
// Neither the name of the Event Store LLP nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//  

using System;
using System.Threading;
using EventStore.ClientAPI.Common.Log;
using EventStore.ClientAPI.Common.Utils;
using EventStore.ClientAPI.Core;

namespace EventStore.ClientAPI
{
    /// <summary>
    /// Represents a subscription to some particular stream or to all possible streams within the Event Store
    /// </summary>
    public class EventStoreSubscription : IDisposable
    {
        private readonly ILogger Log = LogManager.GetLogger();

        public bool IsSubscribedToAll { get { return _streamId == string.Empty; } }
        public string StreamId { get { return _streamId; } }

        public long LastCommitPosition
        {
            get
            {
                if (_commitPosition == long.MinValue)
                    throw new InvalidOperationException("Subscription wasn't confirmed yet.");
                return _commitPosition;
            }
        }

        public int? LastEventNumber
        {
            get
            {
                if (_commitPosition == long.MinValue)
                    throw new InvalidOperationException("Subscription wasn't confirmed yet.");
                return _eventNumber;
            }
        }

        private readonly Guid _correlationId;
        private readonly string _streamId;
        private readonly SubscriptionsChannel _subscriptionsChannel;
        private readonly Action<EventStoreSubscription, ResolvedEvent> _eventAppeared;
        private readonly Action<EventStoreSubscription> _subscriptionDropped;
        
        private long _commitPosition = long.MinValue;
        private int? _eventNumber;
        private volatile int _unsubscribed;

        internal EventStoreSubscription(Guid correlationId, 
                                        string streamId, 
                                        SubscriptionsChannel subscriptionsChannel,
                                        Action<EventStoreSubscription, ResolvedEvent> eventAppeared, 
                                        Action<EventStoreSubscription> subscriptionDropped)
        {
            Ensure.NotEmptyGuid(correlationId, "correlationId");
            Ensure.NotNull(streamId, "stream");
            Ensure.NotNull(subscriptionsChannel, "subscriptionsChannel");
            Ensure.NotNull(eventAppeared, "eventAppeared");
            
            _correlationId = correlationId;
            _streamId = streamId;
            _subscriptionsChannel = subscriptionsChannel;
            _eventAppeared = eventAppeared;
            _subscriptionDropped = subscriptionDropped ?? (x => {});
        }

        public void Dispose()
        {
            Unsubscribe();
        }

        public void Close()
        {
            Unsubscribe();
        }

        public void Unsubscribe()
        {
#pragma warning disable 420
            if (Interlocked.CompareExchange(ref _unsubscribed, 1, 0) == 0)
            {
                Log.Debug("Subscription {0:B} to {1}: unsubscribing...", _correlationId, IsSubscribedToAll ? "<all>" : StreamId);

                _subscriptionsChannel.Unsubscribe(_correlationId);
                _subscriptionDropped(this);
            }
#pragma warning restore 420
        }

        internal void ConfirmSubscription(long commitPosition, int? eventNumber)
        {
            if (commitPosition < -1)
                throw new ArgumentOutOfRangeException("commitPosition", string.Format("Invalid commitPosition {0} on subscription confirmation.", commitPosition));

            Log.Debug("Subscription {0:B} to {1}: subscribed at CommitPosition: {2}, EventNumber: {3}.", 
                      _correlationId, IsSubscribedToAll ? "<all>" : StreamId, commitPosition, eventNumber);

            _commitPosition = commitPosition;
            _eventNumber = eventNumber;
        }

        internal void EventAppeared(ResolvedEvent @event)
        {
            if (_unsubscribed != 0)
                return;
            Log.Debug("Subscription {0:B} to {1}: event appeared ({2}, {3}, {4} @ {5}).",
                      _correlationId, IsSubscribedToAll ? "<all>" : StreamId, 
                      @event.OriginalStreamId, @event.OriginalEventNumber, @event.OriginalEvent.EventType, @event.OriginalPosition);
            _eventAppeared(this, @event);
        }

        internal void SubscriptionDropped()
        {
            Log.Debug("Subscription {0:B} to {1}: subscription dropped...", _correlationId, IsSubscribedToAll ? "<all>" : StreamId);
            Unsubscribe();
        }
    }
}
