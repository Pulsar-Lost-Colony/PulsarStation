using Content.Shared._Pulsar.CCVar;
using Content.Shared.Input;
using Content.Shared._Pulsar.VoiceChat;
using OpenTK.Audio.OpenAL;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;

namespace Content.Client._Pulsar.VoiceChat;

public sealed class VoiceChatSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _players = default!;

    private const int SampleRate = 16_000;
    private const int ChunkSamples = 1024;

    private IntPtr _captureDevice;
    private bool _captureStarted;
    private bool _pushToTalk;
    private float _voiceVolume = 1f;

    private readonly Dictionary<NetEntity, int> _speakerSources = new();

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;

        SubscribeNetworkEvent<VoiceChatAudioChunkEvent>(OnAudioChunk);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.PushToTalk, InputCmdHandler.FromDelegate(HandlePushToTalk, handle: false, outsidePrediction: true))
            .Register<VoiceChatSystem>();

        Subs.CVar(_cfg, CCVars.VoiceChatVolume, volume => _voiceVolume = volume, true);
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<VoiceChatSystem>();

        foreach (var source in _speakerSources.Values)
        {
            AL.SourceStop(source);
            AL.DeleteSource(source);
        }

        _speakerSources.Clear();

        StopCapture();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        if (!_pushToTalk || !_cfg.GetCVar(CCVars.VoiceChatClientEnabled) || !_cfg.GetCVar(CCVars.VoiceChatEnabled))
            return;

        EnsureCapture();
        if (!_captureStarted)
            return;

        var available = ALC.GetInteger(_captureDevice, AlcGetInteger.CaptureSamples);
        if (available < ChunkSamples)
            return;

        var samples = new short[ChunkSamples];
        ALC.CaptureSamples(_captureDevice, samples, ChunkSamples);

        var data = new byte[samples.Length * sizeof(short)];
        Buffer.BlockCopy(samples, 0, data, 0, data.Length);

        var entity = _players.LocalEntity;
        if (entity is not { Valid: true })
            return;

        RaiseNetworkEvent(new VoiceChatAudioChunkEvent(GetNetEntity(entity.Value), data, SampleRate));
    }

    private bool HandlePushToTalk(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        _pushToTalk = args.State == BoundKeyState.Down;
        if (!_pushToTalk)
            StopCapture();

        return false;
    }

    private void EnsureCapture()
    {
        if (_captureStarted)
            return;

        _captureDevice = ALC.CaptureOpenDevice(null, SampleRate, ALFormat.Mono16, SampleRate);
        if (_captureDevice == IntPtr.Zero)
            return;

        ALC.CaptureStart(_captureDevice);
        _captureStarted = true;
    }

    private void StopCapture()
    {
        if (!_captureStarted)
            return;

        ALC.CaptureStop(_captureDevice);
        ALC.CaptureCloseDevice(_captureDevice);
        _captureDevice = IntPtr.Zero;
        _captureStarted = false;
    }

    private void OnAudioChunk(VoiceChatAudioChunkEvent ev)
    {
        if (!_cfg.GetCVar(CCVars.VoiceChatClientEnabled))
            return;

        if (!_speakerSources.TryGetValue(ev.Speaker, out var source))
        {
            source = AL.GenSource();
            _speakerSources[ev.Speaker] = source;
        }

        AL.Source(source, ALSourcef.Gain, _voiceVolume);

        var buffer = AL.GenBuffer();
        var samples = new short[ev.Data.Length / sizeof(short)];
        Buffer.BlockCopy(ev.Data, 0, samples, 0, ev.Data.Length);
        AL.BufferData(buffer, ALFormat.Mono16, samples, ev.SampleRate);
        AL.SourceQueueBuffer(source, buffer);

        AL.GetSource(source, ALGetSourcei.SourceState, out var state);
        if ((ALSourceState) state != ALSourceState.Playing)
            AL.SourcePlay(source);

        AL.GetSource(source, ALGetSourcei.BuffersProcessed, out var processed);
        while (processed-- > 0)
        {
            var consumed = AL.SourceUnqueueBuffer(source);
            AL.DeleteBuffer(consumed);
        }
    }
}
