using Content.Shared._Pulsar.CCVar;
using Content.Shared.Input;
using Content.Shared._Pulsar.VoiceChat;
using OpenTK.Audio.OpenAL;
using Robust.Client.Audio;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Input.Binding;
using Robust.Shared.Utility;
using System.IO;
using Robust.Shared.Input;
using Robust.Shared.Player;
using Robust.Shared.Map;

namespace Content.Client._Pulsar.VoiceChat;

public sealed class VoiceChatSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IResourceManager _res = default!;
    [Dependency] private readonly AudioSystem _audio = default!;

    private const int SampleRate = 16_000;
    private const int ChunkSamples = 1024;

    private static readonly MemoryContentRoot ContentRoot = new();
    private static readonly ResPath Prefix = ResPath.Root / "VoiceChat";
    private static bool _contentRootAdded;

    private ALCaptureDevice _captureDevice;
    private bool _captureStarted;
    private bool _pushToTalk;
    private float _voiceVolume = 1f;
    private int _chunkId;

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;

        if (!_contentRootAdded)
        {
            _contentRootAdded = true;
            _res.AddRoot(Prefix, ContentRoot);
        }

        SubscribeNetworkEvent<VoiceChatAudioChunkEvent>(OnAudioChunk);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.PushToTalk, new PointerStateInputCmdHandler(HandlePushToTalkDown, HandlePushToTalkUp, outsidePrediction: true))
            .Register<VoiceChatSystem>();


        Subs.CVar(_cfg, CCVars.VoiceChatVolume, volume => _voiceVolume = volume, true);
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<VoiceChatSystem>();
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

    private bool HandlePushToTalkDown(ICommonSession? session, EntityCoordinates coordinates, EntityUid uid)
    {
        _pushToTalk = true;
        return false;
    }

    private bool HandlePushToTalkUp(ICommonSession? session, EntityCoordinates coordinates, EntityUid uid)
    {
        _pushToTalk = false;
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
        _captureDevice = new(IntPtr.Zero);
        _captureStarted = false;
    }

    private void OnAudioChunk(VoiceChatAudioChunkEvent ev)
    {
        if (!_cfg.GetCVar(CCVars.VoiceChatClientEnabled))
            return;

        if (!TryGetEntity(ev.Speaker, out var speaker) || !(speaker?.Valid ?? false) || Deleted(speaker))
            return;

        var samples = new short[ev.Data.Length / sizeof(short)];
        Buffer.BlockCopy(ev.Data, 0, samples, 0, ev.Data.Length);

        var chunkPath = new ResPath($"{_chunkId++}.wav");
        ContentRoot.AddOrUpdateFile(chunkPath, BuildWav(samples, ev.SampleRate));

        var resource = new AudioResource();
        resource.Load(IoCManager.Instance!, Prefix / chunkPath);

        var soundSpecifier = new ResolvedPathSpecifier(Prefix / chunkPath);
        var audioParams = AudioParams.Default.WithVolume(SharedAudioSystem.GainToVolume(_voiceVolume));

        _audio.PlayEntity(resource.AudioStream, speaker.Value, soundSpecifier, audioParams);
        ContentRoot.RemoveFile(chunkPath);
    }

    private static byte[] BuildWav(short[] samples, int sampleRate)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        const short channels = 1;
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        var dataLength = samples.Length * sizeof(short);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
