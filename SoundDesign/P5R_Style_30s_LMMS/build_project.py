from __future__ import annotations

from pathlib import Path
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
    pattern_len: int = SONG_LEN,
    pattern_type: int = 1,
    pattern_steps: int = 16,
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
        {"type": str(pattern_type), "muted": "0", "steps": str(pattern_steps), "name": name, "pos": "0", "len": str(pattern_len)},
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


def add_drum_beat(container: ET.Element) -> None:
    # 一个顶层鼓轨里放四个真实鼓采样，避免为了节奏把 Song Editor 堆成一排鼓轨。
    track = ET.SubElement(container, "track", {"type": "1", "muted": "0", "name": "Drums", "solo": "0"})
    bbtrack = ET.SubElement(track, "bbtrack")
    drum_container = ET.SubElement(
        bbtrack,
        "trackcontainer",
        {"visible": "0", "width": "720", "height": "320", "type": "bbtrackcontainer", "x": "20", "y": "180", "maximized": "0", "minimized": "0"},
    )

    two_bars = TICKS_PER_BAR * 2

    kick = [
        (57, p, -192, v)
        for p, v in (
            (0, 108), (36, 84), (84, 92), (96, 105), (132, 82), (180, 78),
            (192, 108), (252, 88), (288, 104), (324, 82), (360, 86),
        )
    ]
    snare = [
        (57, p, -192, v)
        for p, v in (
            (48, 112), (132, 42), (144, 116), (180, 36),
            (240, 112), (312, 40), (336, 118),
        )
    ]

    hat: list[tuple[int, int, int, int]] = []
    for base in (0, 192):
        for index, p in enumerate(range(0, 192, 24)):
            hat.append((57, base + p, -192, 64 if index % 2 == 0 else 48))
        # 两个很轻的 16 分音符让律动有向前感，但不把鼓写满。
        hat.append((57, base + 36, -192, 36))
        hat.append((57, base + 132, -192, 40))

    open_hat = [(57, 180, -192, 58), (57, 372, -192, 62)]

    add_track(drum_container, "Kick", kick, volume=58, sample="drums/bassdrum_acoustic01.ogg", pattern_len=two_bars, pattern_type=0, pattern_steps=32)
    add_track(drum_container, "Snare", snare, volume=52, sample="drums/snare_acoustic01.ogg", pattern_len=two_bars, pattern_type=0, pattern_steps=32)
    add_track(drum_container, "Closed Hat", hat, volume=34, pan=-8, sample="drums/hihat_closed02.ogg", pattern_len=two_bars, pattern_type=0, pattern_steps=32)
    add_track(drum_container, "Open Hat", open_hat, volume=30, pan=10, sample="drums/hihat_opened01.ogg", pattern_len=two_bars, pattern_type=0, pattern_steps=32)

    ET.SubElement(
        track,
        "bbtco",
        {"usestyle": "1", "muted": "0", "name": "Drum Groove", "pos": "0", "len": str(SONG_LEN), "color": "4282417407"},
    )


def build_music() -> dict[str, list[tuple[int, int, int, int]]]:
    tracks: dict[str, list[tuple[int, int, int, int]]] = {
        "Slap Bass": [],
        "Piano": [],
        "Guitar": [],
    }

    progression = [
        ((53, 56, 60, 63, 67), 41, 44),  # Fm9
        ((49, 53, 56, 60, 63), 37, 41),  # Dbmaj9
        ((51, 55, 58, 60, 65), 39, 43),  # Eb13-ish
        ((48, 52, 55, 58, 63), 36, 39),  # C7#9
        ((53, 56, 60, 63, 67), 41, 44),
        ((46, 49, 53, 56, 60), 46, 49),  # Bbm9，抬高八度避免突然下坠
        ((49, 53, 56, 60, 63), 49, 53),  # Dbmaj9，延续上行线条
        ((48, 52, 55, 58, 63), 48, 51),  # C7#9
    ] * 2

    # 钢琴明确打在反拍上，把空间留给鼓和贝斯。
    chord_hits = ((72, 24, 64), (168, 20, 70))

    for bar, (chord, root, chord_tone) in enumerate(progression, start=1):
        tracks["Piano"].extend(chord_notes(chord, bar, chord_hits))

        next_root = progression[bar % len(progression)][1]
        direction = 1 if next_root > root else -1 if next_root < root else 0
        approach = next_root - direction if direction != 0 else root
        midpoint = round((root + next_root) / 2)

        # 小节内使用邻近和弦音，不再反复跳五度或八度。
        if bar % 2 == 1:
            bass_pattern = [
                (root, 0, 28, 100),
                (chord_tone, 36, 14, 70),
                (root, 84, 12, 82),
                (root, 96, 24, 94),
            ]
            transition_tick = 174
        else:
            bass_pattern = [
                (root, 0, 28, 99),
                (chord_tone, 60, 14, 68),
                (root, 96, 26, 92),
            ]
            transition_tick = 168

        # 只在真正有距离时补经过音；大跳用两级过渡，小跳只用一级半音导向。
        transition_notes: list[int] = []
        if abs(next_root - root) >= 4 and midpoint not in (root, next_root):
            transition_notes.append(midpoint)
        if abs(next_root - root) >= 2 and approach not in (root, next_root) and approach not in transition_notes:
            transition_notes.append(approach)

        if len(transition_notes) == 2:
            bass_pattern.append((transition_notes[0], 144, 12, 58))
            bass_pattern.append((transition_notes[1], transition_tick, 10, 52))
        elif len(transition_notes) == 1:
            bass_pattern.append((transition_notes[0], transition_tick, 12, 54))

        for key, tick, length, velocity in bass_pattern:
            tracks["Slap Bass"].append((key, bar_pos(bar, tick), length, velocity))

    # 吉他只在句尾做很短的回答，避免和钢琴抢节奏。
    melody = {
        4: [(68, 120, 14), (72, 150, 18)],
        8: [(67, 120, 14), (70, 150, 18)],
        12: [(68, 120, 14), (72, 150, 18)],
        16: [(67, 114, 14), (70, 144, 14), (72, 168, 18)],
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

    tracks = build_music()

    add_drum_beat(container)
    add_track(container, "Slap Bass", tracks["Slap Bass"], volume=42, pan=0, sample="instruments/bassslap01.ogg")
    add_track(container, "Piano", tracks["Piano"], volume=31, pan=-8, sample="instruments/piano01.ogg")
    add_track(container, "Guitar", tracks["Guitar"], volume=24, pan=10, sample="instruments/steel_guitar01.ogg")

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
