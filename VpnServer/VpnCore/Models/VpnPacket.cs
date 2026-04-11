using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace VpnCore.Models {
    /// <summary>
    /// Типы пакетов VPN протокола
    /// Каждый тип имеет свое назначение и обрабатывается соответствующим образом
    /// </summary>
    public enum PacketType : byte {
        Data = 0x01,              // Обычные пользовательские данные
        Handshake = 0x02,         // Начало рукопожатия (клиент->сервер)
        HandshakeResponse = 0x03, // Ответ на рукопожатие (сервер->клиент)
        KeepAlive = 0x04,         // Поддержание соединения (ping/pong)
        Disconnect = 0x05,        // Корректное завершение соединения
        Auth = 0x06,              // Аутентификационные данные
        AuthResponse = 0x07,      // Ответ на аутентификацию
        Ping = 0x08,              // Запрос задержки
        Pong = 0x09,              // Ответ на ping (содержит время)
        Error = 0x0A,             // Сообщение об ошибке
        Control = 0x0B,           // Управляющие команды
        Fragment = 0x0C,          // Фрагмент большого пакета
        FragmentAck = 0x0D,       // Подтверждение получения фрагмента
        MultiplexStream = 0x0E,   // Создание нового мультиплексного потока
        MultiplexData = 0x0F,     // Данные в мультиплексном потоке
        MultiplexClose = 0x10,    // Закрытие мультиплексного потока
        Compression = 0x11        // Сжатые данные
    }

    /// <summary>
    /// Приоритет пакета для QoS (Quality of Service)
    /// Пакеты с высоким приоритетом отправляются в первую очередь
    /// </summary>
    public enum PacketPriority : byte {
        Low = 0,      // Низкий приоритет (фоновый трафик)
        Normal = 1,   // Обычный приоритет (интернет-серфинг)
        High = 2,     // Высокий приоритет (видеозвонки, голос)
        Critical = 3  // Критический приоритет (рукопожатие, управление)
    }

    /// <summary>
    /// Основной класс VPN пакета
    /// Содержит все необходимые поля для надежной передачи через UDP
    /// Поддерживает фрагментацию, мультиплексирование и контроль целостности
    /// </summary>
    public sealed class VpnPacket {
        // Размер заголовка в байтах:
        // Type(1) + Priority(1) + Flags(2) + PacketId(4) + Timestamp(4) + Length(4) = 16 байт
        private const int HeaderSize = 1 + 1 + 2 + 4 + 4 + 4;

        // === Основные поля пакета (сериализуются) ===

        /// <summary>Тип пакета (Data, Handshake, KeepAlive и т.д.)</summary>
        public PacketType Type { get; set; }

        /// <summary>Приоритет для QoS (Low, Normal, High, Critical)</summary>
        public PacketPriority Priority { get; set; }

        /// <summary>Битовые флаги для дополнительных опций</summary>
        public ushort Flags { get; set; }

        /// <summary>Уникальный идентификатор пакета (для отслеживания и anti-replay)</summary>
        public uint PacketId { get; set; }

        /// <summary>Unix timestamp создания пакета (для расчета задержки)</summary>
        public uint Timestamp { get; set; }

        /// <summary>Полезная нагрузка (фактические данные)</summary>
        public byte[] Data { get; set; }

        /// <summary>Контрольная сумма SHA-256 (для проверки целостности)</summary>
        public byte[] Checksum { get; set; }

        // === Метаданные (не сериализуются, используются внутри) ===

        /// <summary>Время создания пакета (локальное)</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Время отправки пакета</summary>
        public DateTime SentAt { get; set; }

        /// <summary>Время получения пакета</summary>
        public DateTime ReceivedAt { get; set; }

        /// <summary>Сколько раз пакет был переотправлен (для надежности)</summary>
        public int RetransmitCount { get; set; }

        /// <summary>Подтвержден ли пакет получателем</summary>
        public bool IsAcknowledged { get; set; }

        // === Информация для фрагментации (разбивка больших пакетов) ===

        /// <summary>Является ли этот пакет фрагментом</summary>
        public bool IsFragment { get; set; }

        /// <summary>ID фрагментированного сообщения (общий для всех фрагментов)</summary>
        public ushort FragmentId { get; set; }

        /// <summary>Смещение фрагмента в исходном сообщении (в байтах)</summary>
        public ushort FragmentOffset { get; set; }

        /// <summary>Общее количество фрагментов в сообщении</summary>
        public ushort TotalFragments { get; set; }

        // === Информация для мультиплексирования (несколько потоков через одно соединение) ===

        /// <summary>ID потока (для разделения трафика разных приложений)</summary>
        public ushort StreamId { get; set; }

        /// <summary>Номер последовательности в потоке (для упорядочивания)</summary>
        public uint StreamSequence { get; set; }

        /// <summary>
        /// Конструктор по умолчанию
        /// Автоматически генерирует ID пакета и временную метку
        /// </summary>
        public VpnPacket() {
            PacketId = GeneratePacketId();
            Timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            CreatedAt = DateTime.UtcNow;
            Priority = PacketPriority.Normal;
            Data = Array.Empty<byte>();
            Checksum = Array.Empty<byte>();
        }

        /// <summary>
        /// Конструктор с типом и данными
        /// </summary>
        /// <param name="type">Тип пакета</param>
        /// <param name="data">Полезная нагрузка</param>
        public VpnPacket(PacketType type, byte[] data) : this() {
            Type = type;
            Data = data ?? Array.Empty<byte>();
        }

        /// <summary>
        /// Генерация уникального ID пакета
        /// MethodImplOptions.AggressiveInlining - просит компилятор встроить метод для производительности
        /// Использует младшие 32 бита текущего времени (достаточно для anti-replay)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint GeneratePacketId() {
            // Ticks = 100-наносекундные интервалы с 0001 года
            // & 0xFFFFFFFF - берем только младшие 32 бита
            return (uint)(DateTime.UtcNow.Ticks & 0xFFFFFFFF);
        }

        /// <summary>
        /// Сериализация пакета в массив байт для отправки по сети
        /// Формат: [Header][Data][Checksum]
        /// </summary>
        public byte[] Serialize() {
            var dataLength = Data?.Length ?? 0;
            var totalSize = HeaderSize + dataLength + 32; // +32 для SHA-256 хеша
            var buffer = new byte[totalSize];
            var offset = 0;

            // === Запись заголовка ===

            // 1 байт: Тип пакета
            buffer[offset++] = (byte)Type;

            // 1 байт: Приоритет
            buffer[offset++] = (byte)Priority;

            // 2 байта: Флаги (Big Endian - сетевой порядок байт)
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset), Flags);
            offset += 2;

            // 4 байта: ID пакета
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), PacketId);
            offset += 4;

            // 4 байта: Timestamp
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset), Timestamp);
            offset += 4;

            // 4 байта: Длина данных
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(offset), dataLength);
            offset += 4;

            // === Запись данных ===
            if (dataLength > 0) {
                Buffer.BlockCopy(Data, 0, buffer, offset, dataLength);
                offset += dataLength;
            }

            // === Вычисление и запись контрольной суммы ===
            // SHA-256 от всего пакета (включая заголовок и данные, исключая саму сумму)
            var checksum = ComputeChecksum(buffer.AsSpan(0, offset));
            Buffer.BlockCopy(checksum, 0, buffer, offset, 32);

            return buffer;
        }

        /// <summary>
        /// Десериализация пакета из полученных байт
        /// Проверяет контрольную сумму перед распаковкой
        /// </summary>
        /// <param name="buffer">Полученные байты</param>
        /// <returns>Восстановленный пакет</returns>
        /// <exception cref="ArgumentException">Неверный размер буфера</exception>
        /// <exception cref="CryptographicException">Ошибка проверки контрольной суммы</exception>
        public static VpnPacket Deserialize(byte[] buffer) {
            // Проверка минимального размера (заголовок + контрольная сумма)
            if (buffer == null || buffer.Length < HeaderSize + 32)
                throw new ArgumentException("Invalid packet buffer");

            var packet = new VpnPacket();
            var offset = 0;

            // === Чтение заголовка ===
            packet.Type = (PacketType)buffer[offset++];
            packet.Priority = (PacketPriority)buffer[offset++];
            packet.Flags = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(offset));
            offset += 2;
            packet.PacketId = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset));
            offset += 4;
            packet.Timestamp = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(offset));
            offset += 4;
            var dataLength = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(offset));
            offset += 4;

            // === Проверка контрольной суммы ===
            // Вычисляем хеш от полученных данных (без самого хеша)
            var checksumOffset = offset + dataLength;
            var receivedChecksum = new byte[32];
            Buffer.BlockCopy(buffer, checksumOffset, receivedChecksum, 0, 32);

            var computedChecksum = ComputeChecksum(buffer.AsSpan(0, checksumOffset));

            // Сравнение должно быть постоянным по времени (Constant Time)
            // Это предотвращает атаки по времени (timing attacks)
            if (!CompareChecksums(computedChecksum, receivedChecksum))
                throw new CryptographicException("Packet checksum verification failed");

            // === Чтение данных ===
            if (dataLength > 0) {
                packet.Data = new byte[dataLength];
                Buffer.BlockCopy(buffer, offset, packet.Data, 0, dataLength);
            }

            packet.ReceivedAt = DateTime.UtcNow;
            return packet;
        }

        /// <summary>
        /// Вычисление SHA-256 контрольной суммы
        /// MethodImplOptions.AggressiveInlining - встраивание для производительности
        /// </summary>
        /// <param name="data">Данные для хеширования</param>
        /// <returns>32-байтовый хеш</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] ComputeChecksum(Span<byte> data) {
            using var sha256 = SHA256.Create();
            // ToArray() создает копию, но для небольших пакетов это приемлемо
            return sha256.ComputeHash(data.ToArray());
        }

        /// <summary>
        /// Постоянное по времени сравнение двух хешей
        /// Важно для криптографической безопасности
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CompareChecksums(byte[] a, byte[] b) {
            // CryptographicOperations.FixedTimeEquals - защита от timing attacks
            // Сравнивает за одинаковое время, независимо от того, где найдено различие
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        /// <summary>
        /// Строковое представление пакета (для логирования)
        /// </summary>
        public override string ToString() {
            return $"[{Type}] ID:{PacketId} Priority:{Priority} Size:{Data?.Length ?? 0} bytes";
        }
    }
}