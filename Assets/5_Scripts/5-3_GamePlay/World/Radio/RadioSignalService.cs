using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

#region 无线电端点与载荷
[Flags]
public enum RadioEndpointCapabilities
{
    None = 0, TransmitCircuit = 1, ReceiveCircuit = 2, TransmitText = 4, ReceiveText = 8
}
public enum RadioPayloadKind { CircuitBit, Text }

public readonly struct RadioCircuitState
{
    public readonly bool HasSource;
    public readonly bool Value;
    public RadioCircuitState(bool hasSource, bool value) { HasSource = hasSource; Value = value; }
}

public readonly struct RadioCircuitFrame
{
    public readonly string WorldKey;
    public readonly int SourceId, Channel;
    public readonly ulong Epoch, Sequence;
    public readonly Vector2 Position;
    public readonly bool Value;
    public RadioPayloadKind Kind => RadioPayloadKind.CircuitBit;
    public RadioCircuitFrame(string worldKey, int sourceId, ulong epoch, ulong sequence, int channel, Vector2 position, bool value)
    { WorldKey = worldKey; SourceId = sourceId; Epoch = epoch; Sequence = sequence; Channel = channel; Position = position; Value = value; }
}

public readonly struct RadioTextMessage
{
    public readonly string WorldKey;
    public readonly int SourceId, ReceiverId, Channel;
    public readonly ulong Epoch, Sequence;
    public readonly Vector2 SourcePosition;
    public readonly string Sender, Text, MessageId;
    public readonly Vector2 ReceiverPosition;
    public RadioPayloadKind Kind => RadioPayloadKind.Text;
    public RadioTextMessage(string worldKey, int sourceId, int receiverId, ulong epoch, ulong sequence, int channel,
        Vector2 sourcePosition, string sender, string text, string messageId, Vector2 receiverPosition)
    { WorldKey = worldKey; SourceId = sourceId; ReceiverId = receiverId; Epoch = epoch; Sequence = sequence;
        Channel = channel; SourcePosition = sourcePosition; Sender = sender; Text = text; MessageId = messageId; ReceiverPosition = receiverPosition; }
}
#endregion

/// <summary>单个世界按频道传播信号，以模拟时钟调度心跳与来源失效。</summary>
public sealed class RadioSignalService
{
    #region 世界信号状态
    public const int MinimumChannel = 1, MaximumChannel = 16, MaximumTextLength = 160;
    public const float MaximumRange = 128f, HeartbeatSeconds = 1f, SignalLifetimeSeconds = 3f;
    private sealed class SourceFrame
    {
        public ulong Epoch, Sequence;
        public double ReceivedAt;
        public bool Active, Value;
    }
    private sealed class Endpoint
    {
        public int Id, Channel;
        public ulong Epoch, Sequence, TextSequence;
        public Func<Vector2> Position;
        public RadioEndpointCapabilities Capabilities;
        public Action<RadioCircuitState> Circuit;
        public Action<RadioTextMessage> Text;
        public readonly Dictionary<int, SourceFrame> Sources = new();
        public readonly HashSet<Endpoint> Receivers = new();
        public bool HasCircuit, CircuitValue, HasReceivedSource, ReceivedValue, HeartbeatScheduled;
        public RadioCircuitFrame LastFrame;
    }
    private readonly struct Notification
    {
        public readonly Endpoint Endpoint;
        public readonly RadioCircuitState Circuit;
        public readonly RadioTextMessage Text;
        public readonly bool IsText;
        public Notification(Endpoint endpoint, RadioCircuitState circuit)
        { Endpoint = endpoint; Circuit = circuit; Text = default; IsText = false; }
        public Notification(Endpoint endpoint, RadioTextMessage text)
        { Endpoint = endpoint; Circuit = default; Text = text; IsText = true; }
    }
    private readonly struct Timer
    {
        public readonly double Due;
        public readonly ulong Serial;
        public readonly Endpoint Endpoint;
        public readonly int SourceId;
        public readonly SourceFrame Frame;
        public Timer(double due, ulong serial, Endpoint endpoint, int sourceId, SourceFrame frame)
        { Due = due; Serial = serial; Endpoint = endpoint; SourceId = sourceId; Frame = frame; }
    }
    private readonly Dictionary<int, Endpoint> endpoints = new();
    private readonly Dictionary<int, HashSet<Endpoint>> channels = new();
    private readonly Queue<Notification> notifications = new();
    private readonly List<Timer> timers = new();
    private readonly WorldTopologyDomain topology;
    private static long nextEpoch;
    private ulong nextTimerSerial;
    private double elapsed;
    private bool dispatching;
    public string WorldKey { get; }
    public RadioSignalService(string worldKey, WorldTopologyDomain topology)
    { WorldKey = worldKey ?? throw new ArgumentNullException(nameof(worldKey)); this.topology = topology; }
    #endregion

    #region 端点登记与释放
    public bool Register(int id, int channel, Func<Vector2> position, RadioEndpointCapabilities capabilities,
        Action<RadioCircuitState> onCircuit = null, Action<RadioTextMessage> onText = null)
    {
        if (channel < MinimumChannel || channel > MaximumChannel || position == null || endpoints.ContainsKey(id)) return false;
        var endpoint = new Endpoint { Id = id, Channel = channel, Position = position, Capabilities = capabilities,
            Circuit = onCircuit, Text = onText, Epoch = (ulong)Interlocked.Increment(ref nextEpoch) };
        endpoints.Add(id, endpoint);
        if (!channels.TryGetValue(channel, out HashSet<Endpoint> group)) channels.Add(channel, group = new());
        group.Add(endpoint);
        return true;
    }
    public void Unregister(int id)
    {
        if (!endpoints.TryGetValue(id, out Endpoint endpoint)) return;
        endpoints.Remove(id);
        HashSet<Endpoint> group = channels[endpoint.Channel];
        group.Remove(endpoint);
        if (group.Count == 0) channels.Remove(endpoint.Channel);
        foreach (KeyValuePair<int, SourceFrame> pair in endpoint.Sources)
            if (endpoints.TryGetValue(pair.Key, out Endpoint source)) source.Receivers.Remove(endpoint);
        foreach (Endpoint receiver in endpoint.Receivers)
            if (receiver.Sources.Remove(id)) UpdateAggregate(receiver);
        DispatchNotifications();
    }
    public void Clear()
    {
        endpoints.Clear(); channels.Clear(); timers.Clear(); notifications.Clear();
    }
    #endregion

    #region 电路帧与模拟时间
    public bool SendCircuitBit(int id, bool value)
    {
        if (!endpoints.TryGetValue(id, out Endpoint source) ||
            (source.Capabilities & RadioEndpointCapabilities.TransmitCircuit) == 0) return false;
        if (source.HasCircuit && source.CircuitValue == value) return true;
        source.HasCircuit = true; source.CircuitValue = value;
        ScheduleHeartbeat(source);
        DeliverCircuitFrame(source);
        DispatchNotifications();
        return true;
    }
    public bool TryGetLastCircuitFrame(int sourceId, out RadioCircuitFrame frame)
    {
        if (endpoints.TryGetValue(sourceId, out Endpoint source) && source.HasCircuit)
        { frame = source.LastFrame; return true; }
        frame = default;
        return false;
    }
    public bool ReceiveCircuitFrame(RadioCircuitFrame frame)
    {
        bool accepted = ReceiveCircuitFrameInternal(frame);
        DispatchNotifications();
        return accepted;
    }
    private bool ReceiveCircuitFrameInternal(RadioCircuitFrame frame)
    {
        if (frame.WorldKey != WorldKey || !endpoints.TryGetValue(frame.SourceId, out Endpoint source) ||
            (source.Capabilities & RadioEndpointCapabilities.TransmitCircuit) == 0 || frame.Epoch != source.Epoch ||
            frame.Channel != source.Channel || frame.Sequence == 0 || frame.Sequence <= source.Sequence ||
            !float.IsFinite(frame.Position.x) || !float.IsFinite(frame.Position.y)) return false;
        source.Sequence = frame.Sequence; source.LastFrame = frame;
        source.HasCircuit = true; source.CircuitValue = frame.Value;
        ScheduleHeartbeat(source);
        foreach (Endpoint receiver in channels[source.Channel])
        {
            if (receiver.Id == source.Id || (receiver.Capabilities & RadioEndpointCapabilities.ReceiveCircuit) == 0 ||
                !IsWithinRange(frame.Position, receiver.Position())) continue;
            if (!receiver.Sources.TryGetValue(source.Id, out SourceFrame previous))
            {
                receiver.Sources.Add(source.Id, previous = new SourceFrame());
                source.Receivers.Add(receiver);
            }
            if (previous.Epoch > frame.Epoch || previous.Epoch == frame.Epoch && previous.Sequence >= frame.Sequence) continue;
            if (!previous.Active) Schedule(elapsed + SignalLifetimeSeconds, receiver, source.Id, previous);
            bool changed = !previous.Active || previous.Value != frame.Value;
            previous.Epoch = frame.Epoch; previous.Sequence = frame.Sequence;
            previous.ReceivedAt = elapsed; previous.Active = true; previous.Value = frame.Value;
            if (changed) UpdateAggregate(receiver);
        }
        return true;
    }
    private void DeliverCircuitFrame(Endpoint source)
    {
        RadioCircuitFrame frame = new(WorldKey, source.Id, source.Epoch, source.Sequence + 1,
            source.Channel, source.Position(), source.CircuitValue);
        ReceiveCircuitFrameInternal(frame);
    }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f) return;
        elapsed += seconds;
        while (timers.Count > 0 && timers[0].Due <= elapsed)
        {
            Timer timer = PopTimer();
            if (!endpoints.TryGetValue(timer.Endpoint.Id, out Endpoint current) || !ReferenceEquals(current, timer.Endpoint)) continue;
            if (timer.Frame == null)
            {
                if (!current.HasCircuit) { current.HeartbeatScheduled = false; continue; }
                DeliverCircuitFrame(current);
                if (endpoints.TryGetValue(current.Id, out Endpoint live) && ReferenceEquals(live, current))
                {
                    double next = timer.Due + (Math.Floor((elapsed - timer.Due) / HeartbeatSeconds) + 1d) * HeartbeatSeconds;
                    Schedule(next, current, 0, null);
                }
            }
            else if (current.Sources.TryGetValue(timer.SourceId, out SourceFrame frame) && ReferenceEquals(frame, timer.Frame))
            {
                double expiry = frame.ReceivedAt + SignalLifetimeSeconds;
                if (frame.Active && expiry > elapsed) Schedule(expiry, current, timer.SourceId, frame);
                else if (frame.Active) { frame.Active = false; UpdateAggregate(current); }
            }
        }
        DispatchNotifications();
    }
    private void ScheduleHeartbeat(Endpoint source)
    {
        if (source.HeartbeatScheduled) return;
        source.HeartbeatScheduled = true;
        Schedule(elapsed + HeartbeatSeconds, source, 0, null);
    }
    private void UpdateAggregate(Endpoint receiver)
    {
        bool hasSource = false, value = false;
        foreach (SourceFrame frame in receiver.Sources.Values)
        { if (frame.Active) { hasSource = true; value |= frame.Value; } }
        if (receiver.HasReceivedSource == hasSource && receiver.ReceivedValue == value) return;
        receiver.HasReceivedSource = hasSource; receiver.ReceivedValue = value;
        if (receiver.Circuit != null) notifications.Enqueue(new Notification(receiver, new RadioCircuitState(hasSource, value)));
    }
    #endregion

    #region 到期事件堆
    private static bool Earlier(Timer a, Timer b) => a.Due < b.Due || a.Due == b.Due && a.Serial < b.Serial;
    private void Schedule(double due, Endpoint endpoint, int sourceId, SourceFrame frame)
    {
        var timer = new Timer(due, ++nextTimerSerial, endpoint, sourceId, frame);
        int index = timers.Count; timers.Add(timer);
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (!Earlier(timer, timers[parent])) break;
            timers[index] = timers[parent]; index = parent;
        }
        timers[index] = timer;
    }
    private Timer PopTimer()
    {
        Timer first = timers[0], last = timers[timers.Count - 1];
        timers.RemoveAt(timers.Count - 1);
        if (timers.Count == 0) return first;
        int index = 0;
        while (true)
        {
            int child = index * 2 + 1;
            if (child >= timers.Count) break;
            if (child + 1 < timers.Count && Earlier(timers[child + 1], timers[child])) child++;
            if (!Earlier(timers[child], last)) break;
            timers[index] = timers[child]; index = child;
        }
        timers[index] = last;
        return first;
    }
    #endregion

    #region 文字消息与通知
    public bool SendText(int id, string sender, string text, string messageId = null)
    {
        if (!endpoints.TryGetValue(id, out Endpoint source) ||
            (source.Capabilities & RadioEndpointCapabilities.TransmitText) == 0 ||
            string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextLength) return false;
        Vector2 origin = source.Position();
        ulong sequence = ++source.TextSequence;
        foreach (Endpoint receiver in channels[source.Channel])
        {
            if (receiver.Id == source.Id || receiver.Text == null ||
                (receiver.Capabilities & RadioEndpointCapabilities.ReceiveText) == 0) continue;
            Vector2 destination = receiver.Position();
            if (IsWithinRange(origin, destination)) notifications.Enqueue(new Notification(receiver,
                new RadioTextMessage(WorldKey, id, receiver.Id, source.Epoch, sequence, source.Channel,
                    origin, sender, text, messageId, destination)));
        }
        DispatchNotifications();
        return true;
    }
    private bool IsWithinRange(Vector2 from, Vector2 to) => IsWithinDistance(from, to, MaximumRange);
    public bool IsWithinDistance(Vector2 from, Vector2 to, float distance)
        => distance >= 0f && topology.SqrDistance(new float2(from.x, from.y), new float2(to.x, to.y)) <= distance * distance;
    private void DispatchNotifications()
    {
        if (dispatching) return;
        dispatching = true;
        try
        {
            while (notifications.Count > 0)
            {
                Notification notification = notifications.Dequeue();
                Endpoint endpoint = notification.Endpoint;
                if (!endpoints.TryGetValue(endpoint.Id, out Endpoint current) || !ReferenceEquals(current, endpoint)) continue;
                if (notification.IsText) endpoint.Text?.Invoke(notification.Text);
                else endpoint.Circuit?.Invoke(notification.Circuit);
            }
        }
        finally { dispatching = false; }
    }
    #endregion
}
