#region "copyright"

/*
    Copyright © 2021 - 2026 George Hilios <ghilios+NINA@googlemail.com>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Joko.Plugins.HocusFocus.Utility {

    /// <summary>
    /// A <see cref="TaskScheduler"/> that limits the maximum number of tasks that can execute
    /// concurrently. Based on the canonical Microsoft TPL sample implementation. Used by
    /// <see cref="ParallelExecution"/> to provide a single process-wide CPU-concurrency governor so
    /// that nested or concurrent <see cref="System.Threading.Tasks.Parallel"/> loops collectively
    /// stay within a sensible thread budget instead of each spawning ProcessorCount workers.
    /// </summary>
    public sealed class LimitedConcurrencyLevelTaskScheduler : TaskScheduler {
        // Whether the current thread is processing work items.
        [ThreadStatic]
        private static bool _currentThreadIsProcessingItems;

        // The list of tasks to be executed.
        private readonly LinkedList<Task> _tasks = new LinkedList<Task>();

        // The maximum concurrency level allowed by this scheduler.
        private readonly int _maxDegreeOfParallelism;

        // Whether the scheduler is currently processing work items.
        private int _delegatesQueuedOrRunning = 0;

        /// <summary>
        /// Initializes an instance of the <see cref="LimitedConcurrencyLevelTaskScheduler"/> class
        /// with the specified degree of parallelism.
        /// </summary>
        /// <param name="maxDegreeOfParallelism">The maximum degree of parallelism provided by this scheduler.</param>
        public LimitedConcurrencyLevelTaskScheduler(int maxDegreeOfParallelism) {
            if (maxDegreeOfParallelism < 1) {
                throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));
            }
            _maxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        /// <summary>Queues a task to the scheduler.</summary>
        /// <param name="task">The task to be queued.</param>
        protected sealed override void QueueTask(Task task) {
            // Add the task to the list of tasks to be processed. If there aren't enough
            // delegates currently queued or running to process tasks, schedule another.
            lock (_tasks) {
                _tasks.AddLast(task);
                if (_delegatesQueuedOrRunning < _maxDegreeOfParallelism) {
                    ++_delegatesQueuedOrRunning;
                    NotifyThreadPoolOfPendingWork();
                }
            }
        }

        /// <summary>
        /// Informs the ThreadPool that there's work to be executed for this scheduler.
        /// </summary>
        private void NotifyThreadPoolOfPendingWork() {
            ThreadPool.UnsafeQueueUserWorkItem(_ => {
                // Note that the current thread is now processing work items.
                // This is necessary to enable inlining of tasks into this thread.
                _currentThreadIsProcessingItems = true;
                try {
                    // Process all available items in the queue.
                    while (true) {
                        Task item;
                        lock (_tasks) {
                            // When there are no more items to be processed,
                            // note that we're done processing, and get out.
                            if (_tasks.Count == 0) {
                                --_delegatesQueuedOrRunning;
                                break;
                            }

                            // Get the next item from the queue
                            item = _tasks.First.Value;
                            _tasks.RemoveFirst();
                        }

                        // Execute the task we pulled out of the queue
                        base.TryExecuteTask(item);
                    }
                } finally {
                    // We're done processing items on the current thread
                    _currentThreadIsProcessingItems = false;
                }
            }, null);
        }

        /// <summary>Attempts to execute the specified task on the current thread.</summary>
        /// <param name="task">The task to be executed.</param>
        /// <param name="taskWasPreviouslyQueued">
        /// Whether the task was previously queued to the scheduler.</param>
        /// <returns>Whether the task could be executed on the current thread.</returns>
        protected sealed override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) {
            // If this thread isn't already processing a task, we don't support inlining
            if (!_currentThreadIsProcessingItems) {
                return false;
            }

            // If the task was previously queued, remove it from the queue
            if (taskWasPreviouslyQueued) {
                // Try to run the task.
                if (TryDequeue(task)) {
                    return base.TryExecuteTask(task);
                } else {
                    return false;
                }
            } else {
                return base.TryExecuteTask(task);
            }
        }

        /// <summary>Attempts to remove a previously scheduled task from the scheduler.</summary>
        /// <param name="task">The task to be removed.</param>
        /// <returns>Whether the task could be found and removed.</returns>
        protected sealed override bool TryDequeue(Task task) {
            lock (_tasks) {
                return _tasks.Remove(task);
            }
        }

        /// <summary>Gets the maximum concurrency level supported by this scheduler.</summary>
        public sealed override int MaximumConcurrencyLevel => _maxDegreeOfParallelism;

        /// <summary>Gets an enumerable of the tasks currently scheduled on this scheduler.</summary>
        /// <returns>An enumerable of the tasks currently scheduled.</returns>
        protected sealed override IEnumerable<Task> GetScheduledTasks() {
            bool lockTaken = false;
            try {
                Monitor.TryEnter(_tasks, ref lockTaken);
                if (lockTaken) {
                    return _tasks;
                } else {
                    throw new NotSupportedException();
                }
            } finally {
                if (lockTaken) {
                    Monitor.Exit(_tasks);
                }
            }
        }
    }
}
