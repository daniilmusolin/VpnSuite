using System.Net;

namespace VpnCore.Models {
    /// <summary>
    /// Состояние VPN соединения
    /// Используется для отслеживания жизненного цикла подключения
    /// </summary>
    public enum ConnectionState {
        Disconnected,   // Нет соединения (начальное состояние)
        Connecting,     // Установка UDP/TCP соединения
        Handshaking,    // Обмен ключами и аутентификация
        Established,    // Соединение установлено, трафик идет
        Reconnecting,   // Попытка переподключения после обрыва
        Closing,        // Завершение соединения
        Closed,         // Соединение закрыто
        Failed          // Критическая ошибка, соединение невозможно
    }

    /// <summary>
    /// Роль в VPN сети
    /// Определяет поведение и права стороны
    /// </summary>
    public enum ConnectionRole {
        Client,   // Клиент - инициирует соединение, получает IP от сервера
        Server,   // Сервер - принимает соединения, раздает IP, маршрутизирует трафик
        Peer      // Равноправный узел (P2P VPN, например WireGuard)
    }

    /// <summary>
    /// Полная информация о VPN соединении
    /// Содержит метрики, статистику, настройки и диагностику
    /// Используется для мониторинга и отображения в UI
    /// </summary>
    public sealed class ConnectionInfo {
        // === Идентификация ===

        /// <summary>Уникальный идентификатор соединения (GUID)</summary>
        public Guid ConnectionId { get; set; }

        /// <summary>Понятное имя соединения (например "Office VPN")</summary>
        public string Name { get; set; }

        /// <summary>Текущее состояние соединения</summary>
        public ConnectionState State { get; set; }

        /// <summary>Роль (Client/Server/Peer)</summary>
        public ConnectionRole Role { get; set; }

        // === Сетевые адреса ===

        /// <summary>Локальный IP адрес (реальный интерфейс)</summary>
        public IPAddress LocalAddress { get; set; }

        /// <summary>Локальный порт (обычно случайный для клиента, фиксированный для сервера)</summary>
        public int LocalPort { get; set; }

        /// <summary>Удаленный IP адрес сервера или пира</summary>
        public IPAddress RemoteAddress { get; set; }

        /// <summary>Удаленный порт (обычно 51820 для WireGuard, 443 для OpenVPN)</summary>
        public int RemotePort { get; set; }

        /// <summary>Виртуальный IP в VPN сети (например 10.8.0.2)</summary>
        public IPAddress VirtualAddress { get; set; }

        // === Временные метки ===

        /// <summary>Время создания объекта ConnectionInfo</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Время успешного установления соединения</summary>
        public DateTime ConnectedAt { get; set; }

        /// <summary>Время последней активности (отправки или получения данных)</summary>
        public DateTime LastActivityAt { get; set; }

        /// <summary>Время последнего успешного рукопожатия</summary>
        public DateTime LastHandshakeAt { get; set; }

        /// <summary>Длительность соединения (вычисляется автоматически)</summary>
        public TimeSpan Uptime => DateTime.UtcNow - ConnectedAt;

        // === Метрики производительности ===

        /// <summary>Round Trip Time - время туда-обратно в миллисекундах</summary>
        public int Rtt { get; set; }

        /// <summary>Вариация RTT (для расчета таймаутов)</summary>
        public int RttVariance { get; set; }

        /// <summary>Процент потерянных пакетов (0-100)</summary>
        public double PacketLoss { get; set; }

        /// <summary>Джиттер - вариация задержки между пакетами</summary>
        public double Jitter { get; set; }

        // === Пропускная способность ===

        /// <summary>Всего отправлено байт за сессию</summary>
        public long BytesSent { get; set; }

        /// <summary>Всего получено байт за сессию</summary>
        public long BytesReceived { get; set; }

        /// <summary>Текущая скорость отправки (байт/сек)</summary>
        public double CurrentSendSpeed { get; set; }

        /// <summary>Текущая скорость получения (байт/сек)</summary>
        public double CurrentReceiveSpeed { get; set; }

        /// <summary>Средняя скорость отправки за сессию</summary>
        public double AverageSendSpeed { get; set; }

        /// <summary>Средняя скорость получения за сессию</summary>
        public double AverageReceiveSpeed { get; set; }

        // === Качество соединения ===

        /// <summary>Сила сигнала (0-100) - для мобильных сетей или Wi-Fi</summary>
        public int SignalStrength { get; set; }

        /// <summary>Общее качество соединения (0-100) - вычисляется из RTT, потерь, скорости</summary>
        public int ConnectionQuality { get; set; }

        // === Безопасность ===

        /// <summary>Используемый шифр (например "AES-256-GCM", "ChaCha20-Poly1305")</summary>
        public string CipherSuite { get; set; }

        /// <summary>Протокол рукопожатия (например "Noise_IK", "WireGuard")</summary>
        public string HandshakeProtocol { get; set; }

        /// <summary>Включено ли шифрование</summary>
        public bool IsEncrypted { get; set; }

        /// <summary>Пройдена ли аутентификация</summary>
        public bool IsAuthenticated { get; set; }

        /// <summary>Время последней ротации ключей (Perfect Forward Secrecy)</summary>
        public DateTime KeyRotationAt { get; set; }

        // === Статистика пакетов ===

        /// <summary>Всего отправлено пакетов</summary>
        public uint PacketsSent { get; set; }

        /// <summary>Всего получено пакетов</summary>
        public uint PacketsReceived { get; set; }

        /// <summary>Всего потеряно пакетов</summary>
        public uint PacketsLost { get; set; }

        /// <summary>Всего переотправлено пакетов (из-за потерь)</summary>
        public uint PacketsRetransmitted { get; set; }

        /// <summary>Отправлено KeepAlive пакетов</summary>
        public uint HeartbeatSent { get; set; }

        /// <summary>Получено KeepAlive пакетов</summary>
        public uint HeartbeatReceived { get; set; }

        // === Переподключение ===

        /// <summary>Текущее количество попыток переподключения</summary>
        public int ReconnectAttempts { get; set; }

        /// <summary>Максимальное количество попыток переподключения</summary>
        public int MaxReconnectAttempts { get; set; }

        /// <summary>Задержка между попытками переподключения</summary>
        public TimeSpan ReconnectDelay { get; set; }

        // === MTU (Maximum Transmission Unit) ===

        /// <summary>Текущий MTU (максимальный размер пакета без фрагментации)</summary>
        public int CurrentMtu { get; set; }

        /// <summary>Оптимальный MTU (определяется автоматически через Path MTU Discovery)</summary>
        public int OptimalMtu { get; set; }

        /// <summary>Максимально возможный MTU (обычно 1500 для Ethernet)</summary>
        public int MaxMtu { get; set; }

        // === Флаги состояния ===

        /// <summary>Находится ли в процессе переподключения</summary>
        public bool IsReconnecting { get; set; }

        /// <summary>Приостановлено ли соединение (например при потере сети)</summary>
        public bool IsPaused { get; set; }

        /// <summary>Режим низкой задержки (отключает буферизацию для VoIP/Gaming)</summary>
        public bool IsLowLatencyMode { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// Инициализирует значения по умолчанию
        /// </summary>
        public ConnectionInfo() {
            ConnectionId = Guid.NewGuid();           // Генерируем уникальный ID
            CreatedAt = DateTime.UtcNow;             // Запоминаем время создания
            State = ConnectionState.Disconnected;    // Начальное состояние
            CurrentMtu = 1400;                       // Стандартный MTU для VPN
            OptimalMtu = 1400;
            MaxMtu = 1500;                           // Ethernet стандарт
            MaxReconnectAttempts = 5;                // 5 попыток переподключения
            ReconnectDelay = TimeSpan.FromSeconds(1); // Секунда между попытками
            CipherSuite = "AES-256-GCM";             // Современный шифр с аутентификацией
            HandshakeProtocol = "Noise_IK";          // Noise Protocol Framework
            IsEncrypted = true;                      // Шифрование включено по умолчанию
        }

        /// <summary>
        /// Обновление времени последней активности
        /// Вызывается при отправке или получении любого пакета
        /// </summary>
        public void UpdateActivity() {
            LastActivityAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Запись отправленного пакета
        /// Обновляет счетчики байт и пакетов
        /// </summary>
        /// <param name="bytes">Размер отправленного пакета в байтах</param>
        public void RecordPacketSent(int bytes) {
            BytesSent += bytes;      // Атомарно не требуется, т.к. используется в одном потоке
            PacketsSent++;
            UpdateActivity();        // Сбрасываем таймер неактивности
        }

        /// <summary>
        /// Запись полученного пакета
        /// </summary>
        /// <param name="bytes">Размер полученного пакета в байтах</param>
        public void RecordPacketReceived(int bytes) {
            BytesReceived += bytes;
            PacketsReceived++;
            UpdateActivity();
        }

        /// <summary>
        /// Запись потерянного пакета
        /// Автоматически пересчитывает процент потерь
        /// </summary>
        public void RecordPacketLoss() {
            PacketsLost++;
            // Потери в процентах = (потеряно / всего_пакетов) * 100
            PacketLoss = (double)PacketsLost / (PacketsSent + PacketsReceived) * 100;
        }

        /// <summary>
        /// Обновление RTT с использованием сглаживания
        /// Использует алгоритм из TCP: SRTT = α * SRTT + (1-α) * RTT
        /// </summary>
        /// <param name="newRtt">Новое измеренное значение RTT в мс</param>
        public void UpdateRtt(int newRtt) {
            // Коэффициенты сглаживания (из TCP RFC 6298)
            const double alpha = 0.875;  // Для RTT (1/8)
            const double beta = 0.75;    // Для RTT Variance (1/4)

            // RTTVAR = β * RTTVAR + (1-β) * |SRTT - RTT|
            RttVariance = (int)(beta * RttVariance + (1 - beta) * Math.Abs(Rtt - newRtt));

            // SRTT = α * SRTT + (1-α) * RTT
            Rtt = (int)(alpha * Rtt + (1 - alpha) * newRtt);
        }

        /// <summary>
        /// Обновление скоростей передачи данных
        /// Использует экспоненциальное сглаживание для средних значений
        /// </summary>
        /// <param name="interval">Интервал времени между вызовами (обычно 1 секунда)</param>
        public void UpdateSpeeds(TimeSpan interval) {
            var seconds = interval.TotalSeconds;
            if (seconds > 0) {
                // Текущая скорость = байты / время
                CurrentSendSpeed = BytesSent / seconds;
                CurrentReceiveSpeed = BytesReceived / seconds;

                // Экспоненциальное сглаживание (EWMA)
                // new_average = α * new_value + (1-α) * old_average
                const double smoothing = 0.3;  // Коэффициент сглаживания (30% нового, 70% старого)
                AverageSendSpeed = smoothing * CurrentSendSpeed + (1 - smoothing) * AverageSendSpeed;
                AverageReceiveSpeed = smoothing * CurrentReceiveSpeed + (1 - smoothing) * AverageReceiveSpeed;
            }
        }

        /// <summary>
        /// Обновление общего качества соединения
        /// Учитывает RTT, потери пакетов и скорость
        /// Результат от 0 до 100
        /// </summary>
        public void UpdateConnectionQuality() {
            var qualityScore = 100;  // Начинаем с максимального качества

            // === Влияние RTT (чем меньше, тем лучше) ===
            if (Rtt > 300) qualityScore -= 30;      // >300ms - ужасно
            else if (Rtt > 150) qualityScore -= 15;  // 150-300ms - плохо
            else if (Rtt > 50) qualityScore -= 5;    // 50-150ms - нормально
            // <50ms - отлично, штрафа нет

            // === Влияние потери пакетов ===
            // Каждый 1% потерь снижает качество на 2%
            qualityScore -= (int)(PacketLoss * 2);

            // === Влияние скорости ===
            // Если скорость меньше 1 KB/s - это очень плохо
            if (CurrentSendSpeed < 1024 && CurrentReceiveSpeed < 1024)
                qualityScore -= 20;
            // Если скорость меньше 10 KB/s - плохо
            else if (CurrentSendSpeed < 10240 && CurrentReceiveSpeed < 10240)
                qualityScore -= 10;

            // Ограничиваем результат диапазоном 0-100
            ConnectionQuality = Math.Max(0, Math.Min(100, qualityScore));
        }

        /// <summary>
        /// Получение диагностической информации для отладки
        /// Возвращает словарь с ключевыми параметрами
        /// </summary>
        public Dictionary<string, object> GetDiagnostics() {
            return new Dictionary<string, object> {
                ["ConnectionId"] = ConnectionId,
                ["State"] = State.ToString(),
                ["Uptime"] = Uptime.ToString(),
                ["Rtt"] = Rtt,
                ["PacketLoss"] = $"{PacketLoss:F2}%",
                ["Quality"] = ConnectionQuality,
                ["BytesSent"] = BytesSent,
                ["BytesReceived"] = BytesReceived,
                ["SendSpeed"] = $"{CurrentSendSpeed / 1024:F1} KB/s",
                ["ReceiveSpeed"] = $"{CurrentReceiveSpeed / 1024:F1} KB/s",
                ["Mtu"] = CurrentMtu,
                ["Cipher"] = CipherSuite
            };
        }

        /// <summary>
        /// Строковое представление для UI и логов
        /// </summary>
        public override string ToString() {
            return $"[{State}] {RemoteAddress}:{RemotePort} - Quality:{ConnectionQuality}% RTT:{Rtt}ms";
        }
    }
}