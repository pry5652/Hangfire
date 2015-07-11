﻿// This file is part of Hangfire.
// Copyright © 2013-2014 Sergey Odinokov.
// 
// Hangfire is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as 
// published by the Free Software Foundation, either version 3 
// of the License, or any later version.
// 
// Hangfire is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public 
// License along with Hangfire. If not, see <http://www.gnu.org/licenses/>.

using System;
using Hangfire.Common;
using Hangfire.Logging;
using Hangfire.States;

namespace Hangfire.Server
{
    internal class SchedulePoller : IBackgroundProcess
    {
        private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

        private readonly Func<JobStorage, IStateMachineFactory> _stateMachineFactory;
        private readonly TimeSpan _pollInterval;

        private int _enqueuedCount;

        public SchedulePoller(TimeSpan pollInterval)
            : this(pollInterval, StateMachineFactory.Default)
        {
        }

        public SchedulePoller(TimeSpan pollInterval, Func<JobStorage, IStateMachineFactory> stateMachineFactory)
        {
            if (stateMachineFactory == null) throw new ArgumentNullException("stateMachineFactory");

            _stateMachineFactory = stateMachineFactory;
            _pollInterval = pollInterval;
        }

        public void Execute(BackgroundProcessContext context)
        {
            if (!EnqueueNextScheduledJob(context))
            {
                if (_enqueuedCount != 0)
                {
                    Logger.InfoFormat("{0} scheduled jobs were enqueued.", _enqueuedCount);
                    _enqueuedCount = 0;
                }

                context.CancellationToken.WaitHandle.WaitOne(_pollInterval);
            }
            else
            {
                // No wait, try to fetch next scheduled job immediately.
                _enqueuedCount++;
            }
        }

        public override string ToString()
        {
            return "Schedule Poller";
        }

        private bool EnqueueNextScheduledJob(BackgroundProcessContext context)
        {
            using (var connection = context.Storage.GetConnection())
            {
                var timestamp = JobHelper.ToTimestamp(DateTime.UtcNow);

                // TODO: it is very slow. Add batching.
                var jobId = connection
                    .GetFirstByLowestScoreFromSet("schedule", 0, timestamp);

                if (String.IsNullOrEmpty(jobId))
                {
                    return false;
                }

                var stateMachine = _stateMachineFactory(context.Storage).Create(connection);
                var enqueuedState = new EnqueuedState
                {
                    Reason = "Enqueued as a scheduled job"
                };

                stateMachine.ChangeState(jobId, enqueuedState, new[] { ScheduledState.StateName });

                return true;
            }
        }
    }
}