from __future__ import annotations

from pathlib import Path
import math
import struct
import xml.etree.ElementTree as ET


BPM = 134
TICKS_PER_BAR = 192
BARS = 16
SONG_LEN = TICKS_PER_BAR * BARS


ROOT = Path(__file__).resolve().parent
PROJECT_PATH = ROOT / "P5R_Style_30s.mmp"


def add_common_instrument_nodes(
    instrument_track: ET.Element,
    *,
    filter_cutoff: int,
    filter_resonance: float,
    envelope: bool,
    attack: float = 0.0,
    decay: float = 0.25,
    sustain: float = 0.5,
    release: float = 0.12,
) -> None:
    eldata = ET.SubElement(
        instrument_track,
        "eldata",
        {
            "fwet": "1" if filter_cutoff < 14000 else "0",
            "ftype": "7" if filter_cutoff < 14000 else "0",
            "fres": str(filter_resonance),
            "fcut": str(filter_cutoff),
        },
    )
    ET.SubElement(
        eldata,
        "elvol",
        {
            "lspd": "0.1",
            "ctlenvamt": "0",
            "lpdel": "0",
            "pdel": "0",
            "amt": "1" if envelope else "0",
            "hold": "0",
            "syncmode": "0",
            "userwavefile": "",
            "latt": "0",
            "sustain": str(sustain),
            "lamt": "0",
            "lshp": "0",
            "lspd_denominator": "4",
            "lspd_numerator": "4",
            "x100": "0",
            "rel": str(release),
            "dec": str(decay),
            "att": str(attack),
        },
    )
    for name in ("elcut", "elres"):
        ET.SubElement(
            eldata,
            name,
            {
                "lspd": "0.1",
                "ctlenvamt": "0",
                "lpdel": "0",
                "pdel": "0",
                "amt": "0",
                "hold": "0.5",
                "syncmode": "0",
                "userwavefile": "",
                "latt": "0",
                "sustain": "0.5",
                "lamt": "0",
                "lshp": "0",
                "lspd_denominator": "4",
                "lspd_numerator": "4",
                "x100": "0",
                "rel": "0.1",
                "dec": "0.5",
                "att": "0",
            },
        )
    ET.SubElement(instrument_track, "chordcreator", {"chordrange": "1", "chord": "0", "chord-enabled": "0"})
    ET.SubElement(
        instrument_track,
        "arpeggiator",
        {
            "arpdir": "0",
            "arpgate": "100",
            "arptime_denominator": "4",
            "syncmode": "0",
            "arp-enabled": "0",
            "arprange": "1",
            "arptime_numerator": "4",
            "arpmode": "0",
            "arp": "0",
            "arptime": "100",
        },
    )
    ET.SubElement(
        instrument_track,
        "midiport",
        {
            "inputchannel": "0",
            "fixedinputvelocity": "-1",
            "outputcontroller": "0",
            "outputchannel": "1",
            "fixedoutputvelocity": "-1",
            "readable": "0",
            "fixedoutputnote": "-1",
            "outputprogram": "1",
            "writable": "0",
            "basevelocity": "127",
            "inputcontroller": "0",
        },
    )
    ET.SubElement(instrument_track, "fxchain", {"numofeffects": "0", "enabled": "0"})


def add_track(
    container: ET.Element,
    name: str,
    notes: list[tuple[int, int, int, int]],
    *,
    volume: int,
    pan: int = 0,
    sample: str | None = None,
    synth: dict[str, int | str] | None = None,
    filter_cutoff: int = 14000,
    filter_resonance: float = 0.5,
    attack: float = 0.0,
    decay: float = 0.25,
    sustain: float = 0.5,
    release: float = 0.12,
) -> None:
    track = ET.SubElement(container, "track", {"type": "0", "muted": "0", "name": name, "solo": "0"})
    it = ET.SubElement(
        track,
        "instrumenttrack",
        {
            "pitch": "0",
            "vol": str(volume),
            "fxch": "0",
            "pan": str(pan),
            "basenote": "57",
            "usemasterpitch": "1",
            "pitchrange": "1",
        },
    )
    if sample is not None:
        instrument = ET.SubElement(it, "instrument", {"name": "audiofileprocessor"})
        ET.SubElement(
            instrument,
            "audiofileprocessor",
            {
                "interp": "1",
                "reversed": "0",
                "looped": "0",
                "sframe": "0",
                "lframe": "0",
                "eframe": "1",
                "src": sample,
                "amp": "100",
                "stutter": "0",
            },
        )
        add_common_instrument_nodes(it, filter_cutoff=14000, filter_resonance=0.5, envelope=False)
    else:
        params = {
            "finel0": "0",
            "vol1": "55",
            "finel1": "-5",
            "coarse0": "0",
            "finer0": "0",
            "vol2": "30",
            "pan0": "0",
            "finer1": "5",
            "coarse1": "0",
            "finel2": "0",
            "pan1": "0",
            "coarse2": "0",
            "finer2": "0",
            "pan2": "0",
            "wavetype0": "2",
            "wavetype1": "0",
            "wavetype2": "3",
            "phoffset0": "0",
            "phoffset1": "0",
            "phoffset2": "0",
            "modalgo1": "2",
            "modalgo2": "2",
            "modalgo3": "2",
            "stphdetun0": "0",
            "stphdetun1": "0",
            "stphdetun2": "0",
            "userwavefile0": "",
            "userwavefile1": "",
            "userwavefile2": "",
            "vol0": "100",
        }
        if synth:
            params.update({k: str(v) for k, v in synth.items()})
        instrument = ET.SubElement(it, "instrument", {"name": "tripleoscillator"})
        ET.SubElement(instrument, "tripleoscillator", params)
        add_common_instrument_nodes(
            it,
            filter_cutoff=filter_cutoff,
            filter_resonance=filter_resonance,
            envelope=True,
            attack=attack,
            decay=decay,
            sustain=sustain,
            release=release,
        )

    pattern = ET.SubElement(
        track,
        "pattern",
        {"type": "1", "muted": "0", "steps": "16", "name": name, "pos": "0", "len": str(SONG_LEN)},
    )
    for key, pos, length, velocity in notes:
        ET.SubElement(
            pattern,
            "note",
            {"key": str(key), "vol": str(velocity), "pos": str(pos), "pan": "0", "len": str(length)},
        )


def bar_pos(bar: int, tick: int = 0) -> int:
    return (bar - 1) * TICKS_PER_BAR + tick


def chord_notes(chord: tuple[int, ...], bar: int, hits: tuple[tuple[int, int, int], ...]) -> list[tuple[int, int, int, int]]:
    result: list[tuple[int, int, int, int]] = []
    for tick, length, velocity in hits:
        for index, key in enumerate(chord):
            # 高音稍弱一点，让和弦更像键盘分层。
            result.append((key, bar_pos(bar, tick), length, max(35, velocity - index * 3)))
    return result


def ogg_duration_seconds(path: Path) -> float:
    data = path.read_bytes()
    first_page = data.find(b"OggS")
    if first_page < 0:
        raise ValueError(f"Not an Ogg stream: {path}")

    segment_count = data[first_page + 26]
    header_size = 27 + segment_count
    body_size = sum(data[first_page + 27:first_page + 27 + segment_count])
    body = data[first_page + header_size:first_page + header_size + body_size]
    if body[:7] != b"\x01vorbis":
        raise ValueError(f"Unsupported Ogg codec: {path}")

    sample_rate = struct.unpack("<I", body[12:16])[0]
    last_page = data.rfind(b"OggS")
    granule_position = struct.unpack("<Q", data[last_page + 6:last_page + 14])[0]
    return granule_position / sample_rate


def loop_key_for_bar(sample_path: Path) -> int:
    desired_seconds = 60.0 / BPM * 4.0
    sample_seconds = ogg_duration_seconds(sample_path)
    playback_ratio = sample_seconds / desired_seconds
    semitones = round(12.0 * math.log2(playback_ratio))
    return 57 + semitones


def build_music(drum_loop_key: int) -> dict[str, list[tuple[int, int, int, int]]]:
    tracks: dict[str, list[tuple[int, int, int, int]]] = {
        "Acoustic Drum Loop": [],
        "Slap Bass": [],
        "Piano": [],
        "Guitar": [],
    }

    progression = [
        ((53, 56, 60, 63, 67), 41, 48),  # Fm9
        ((49, 53, 56, 60, 63), 37, 44),  # Dbmaj9
        ((51, 55, 58, 60, 65), 39, 46),  # Eb13-ish
        ((48, 52, 55, 58, 63), 36, 43),  # C7#9
        ((53, 56, 60, 63, 67), 41, 48),
        ((46, 49, 53, 56, 60), 34, 41),  # Bbm9
        ((49, 53, 56, 60, 63), 37, 44),
        ((48, 52, 55, 58, 63), 36, 43),
    ] * 2

    chord_hits = ((18, 34, 62), (114, 28, 68))

    for bar, (chord, root, fifth) in enumerate(progression, start=1):
        tracks["Acoustic Drum Loop"].append((drum_loop_key, bar_pos(bar, 0), -192, 74))
        tracks["Piano"].extend(chord_notes(chord, bar, chord_hits))

        next_root = progression[bar % len(progression)][1]
        approach = next_root - 1 if next_root >= root else next_root + 1
        bass_pattern = [
            (root, 0, 26, 96),
            (fifth, 54, 18, 82),
            (root, 102, 24, 92),
            (approach, 168, 14, 78),
        ]
        if bar in (4, 8, 12, 16):
            bass_pattern[-1:] = [(approach, 174, 10, 82)]
        for key, tick, length, velocity in bass_pattern:
            tracks["Slap Bass"].append((key, bar_pos(bar, tick), length, velocity))

    # 吉他只在两个 4 小节段落里回应钢琴，保持编曲有空气感。
    melody = {
        5: [(65, 48, 16), (68, 84, 12), (72, 132, 22)],
        6: [(63, 36, 16), (67, 78, 12), (70, 126, 24)],
        7: [(65, 48, 14), (68, 90, 16), (72, 144, 18)],
        8: [(64, 42, 14), (67, 84, 14), (70, 138, 24)],
        13: [(68, 36, 16), (72, 78, 16), (75, 132, 22)],
        14: [(67, 42, 14), (70, 84, 16), (73, 138, 22)],
        15: [(68, 36, 16), (72, 90, 16), (75, 144, 18)],
        16: [(67, 42, 14), (70, 84, 14), (72, 126, 30)],
    }
    for bar, phrases in melody.items():
        for key, tick, length in phrases:
            tracks["Guitar"].append((key, bar_pos(bar, tick), length, 74 if length < 20 else 80))

    return tracks


def build_project() -> None:
    root = ET.Element(
        "lmms-project",
        {"type": "song", "version": "1.0", "creator": "LMMS", "creatorversion": "1.2.2"},
    )
    ET.SubElement(
        root,
        "head",
        {
            "timesig_denominator": "4",
            "bpm": str(BPM),
            "masterpitch": "0",
            "mastervol": "78",
            "timesig_numerator": "4",
        },
    )
    song = ET.SubElement(root, "song")
    container = ET.SubElement(
        song,
        "trackcontainer",
        {"visible": "1", "width": "1200", "height": "820", "type": "song", "x": "20", "y": "20", "maximized": "0", "minimized": "0"},
    )

    drum_sample_path = Path(r"G:\LMMS\data\samples\beats\break01.ogg")
    drum_loop_key = loop_key_for_bar(drum_sample_path)
    tracks = build_music(drum_loop_key)

    add_track(container, "Acoustic Drum Loop", tracks["Acoustic Drum Loop"], volume=48, sample="beats/break01.ogg")
    add_track(container, "Slap Bass", tracks["Slap Bass"], volume=38, pan=0, sample="instruments/bassslap01.ogg")
    add_track(container, "Piano", tracks["Piano"], volume=29, pan=-10, sample="instruments/piano01.ogg")
    add_track(container, "Guitar", tracks["Guitar"], volume=27, pan=12, sample="instruments/steel_guitar01.ogg")

    mixer = ET.SubElement(song, "fxmixer", {"visible": "0", "width": "647", "height": "332", "x": "9", "y": "441", "maximized": "0", "minimized": "0"})
    master = ET.SubElement(mixer, "fxchannel", {"num": "0", "muted": "0", "volume": "1", "name": "Master", "soloed": "0"})
    ET.SubElement(master, "fxchain", {"numofeffects": "0", "enabled": "0"})
    ET.SubElement(song, "ControllerRackView", {"visible": "0", "width": "258", "height": "173", "x": "664", "y": "444", "maximized": "0", "minimized": "0"})
    ET.SubElement(song, "pianoroll", {"visible": "0", "width": "900", "height": "600", "x": "20", "y": "20", "maximized": "0", "minimized": "0"})
    ET.SubElement(song, "automationeditor", {"visible": "0", "width": "640", "height": "400", "x": "356", "y": "129", "maximized": "0", "minimized": "0"})
    ET.SubElement(song, "projectnotes", {"visible": "0", "width": "408", "height": "394", "x": "9", "y": "16", "maximized": "0", "minimized": "0"})
    ET.SubElement(song, "timeline", {"lp0pos": "0", "lp1pos": str(SONG_LEN), "lpstate": "0"})
    ET.SubElement(song, "controllers")

    tree = ET.ElementTree(root)
    ET.indent(tree, space="  ")
    ROOT.mkdir(parents=True, exist_ok=True)
    tree.write(PROJECT_PATH, encoding="utf-8", xml_declaration=True)
    text = PROJECT_PATH.read_text(encoding="utf-8")
    PROJECT_PATH.write_text(text.replace("<?xml version='1.0' encoding='utf-8'?>", "<?xml version=\"1.0\"?>\n<!DOCTYPE lmms-project>"), encoding="utf-8")
    print(PROJECT_PATH)


if __name__ == "__main__":
    build_project()
