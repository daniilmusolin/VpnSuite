using System.Collections.Concurrent;

namespace VpnCore.Utils {
    /// <summary>
    /// Планировщик задач
    /// Управляет отложенными и периодическими задачами
    /// </summary>
    public sealed class TaskScheduler : IDisposable {
        private static TaskScheduler _instance;
        private static readonly object _lock = new object();

        private readonly ConcurrentDictionary<string, ScheduledTask> _tasks;
        private readonly Timer _timer;
        private readonly Logger _logger;
        private bool _disposed;

        public static TaskScheduler Instance {
            get {
                if (_instance == null) {
                    lock (_lock) {
                        _instance ??= new TaskScheduler();
                    }
                }
                return _instance;
            }
        }

        private TaskScheduler() {
            _tasks = new ConcurrentDictionary<string, ScheduledTask>();
            _timer = new Timer(CheckTasks, null, 100, 100);
            _logger = Logger.Instance;
        }

        /// <summary>
        /// Запуск задачи с задержкой
        /// </summary>
        public string ScheduleOnce(string name, Func<Task> action, TimeSpan delay) {
            var task = new ScheduledTask {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Action = action,
                NextRun = DateTime.UtcNow.Add(delay),
                Interval = TimeSpan.Zero,
                IsRecurring = false
            };

            _tasks[task.Id] = task;
            _logger.Debug($"Scheduled once: {name} in {delay.TotalSeconds}s");
            return task.Id;
        }

        /// <summary>
        /// Запуск периодической задачи
        /// </summary>
        public string ScheduleRecurring(string name, Func<Task> action, TimeSpan interval) {
            var task = new ScheduledTask {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Action = action,
                NextRun = DateTime.UtcNow.Add(interval),
                Interval = interval,
                IsRecurring = true
            };

            _tasks[task.Id] = task;
            _logger.Debug($"Scheduled recurring: {name} every {interval.TotalSeconds}s");
            return task.Id;
        }

        /// <summary>
        /// Отмена задачи
        /// </summary>
        public bool Cancel(string taskId) {
            return _tasks.TryRemove(taskId, out _);
        }

        /// <summary>
        /// Отмена всех задач с указанным именем
        /// </summary>
        public int CancelByName(string name) {
            var ids = _tasks.Where(kvp => kvp.Value.Name == name).Select(kvp => kvp.Key).ToList();
            foreach (var id in ids) {
                _tasks.TryRemove(id, out _);
            }
            return ids.Count;
        }

        /// <summary>
        /// Проверка и выполнение задач
        /// </summary>
        private async void CheckTasks(object state) {
            var now = DateTime.UtcNow;

            foreach (var task in _tasks.Values) {
                if (task.NextRun <= now) {
                    // Запускаем задачу в отдельном потоке
                    _ = RunTask(task);
                }
            }
        }

        private async Task RunTask(ScheduledTask task) {
            try {
                _logger.Debug($"Running task: {task.Name}");
                await task.Action();

                if (task.IsRecurring) {
                    // Перепланируем следующее выполнение
                    task.NextRun = DateTime.UtcNow.Add(task.Interval);
                } else {
                    // Удаляем одноразовую задачу
                    _tasks.TryRemove(task.Id, out _);
                }
            } catch (Exception ex) {
                _logger.Error($"Task {task.Name} failed: {ex.Message}");

                // При ошибке одноразовую задачу удаляем
                if (!task.IsRecurring) {
                    _tasks.TryRemove(task.Id, out _);
                } else {
                    // Для периодической - перепланируем с задержкой
                    task.NextRun = DateTime.UtcNow.Add(TimeSpan.FromSeconds(5));
                }
            }
        }

        /// <summary>
        /// Получение статуса всех задач
        /// </summary>
        public List<TaskStatusInfo> GetStatus() {
            return _tasks.Values.Select(t => new TaskStatusInfo {
                Id = t.Id,
                Name = t.Name,
                NextRun = t.NextRun,
                IsRecurring = t.IsRecurring,
                Interval = t.Interval
            }).ToList();
        }

        public void Dispose() {
            if (!_disposed) {
                _timer?.Dispose();
                _tasks.Clear();
                _disposed = true;
            }
        }

        private class ScheduledTask {
            public string Id { get; set; }
            public string Name { get; set; }
            public Func<Task> Action { get; set; }
            public DateTime NextRun { get; set; }
            public TimeSpan Interval { get; set; }
            public bool IsRecurring { get; set; }
        }
    }

    public class TaskStatusInfo {
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime NextRun { get; set; }
        public bool IsRecurring { get; set; }
        public TimeSpan Interval { get; set; }

        public string NextRunFormatted => NextRun.ToLocalTime().ToString("HH:mm:ss");
        public string IntervalFormatted => IsRecurring ? $"{Interval.TotalSeconds:F0}s" : "One-time";
    }
}