using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 飞船状态转变音效
        private readonly Dictionary<string, (bool Ignited, string Landing)> flightAudioStates = new(StringComparer.Ordinal);
        private readonly List<string> expiredFlightAudioStates = new();
        private bool flightAudioReady;
        private bool flightAudioNetworkBaseline;

        private void ResetFlightAudio()
        {
            flightAudioStates.Clear();
            flightAudioReady = false;
            flightAudioNetworkBaseline = false;
        }

        private void PrimeInitialNetworkFlightAudio()
        {
            if (flightAudioNetworkBaseline) return;
            ResetFlightAudio();
            SynchronizeFlightAudio();
            flightAudioNetworkBaseline = true;
        }

        private void SynchronizeFlightAudio()
        {
            // 首次载入只记基线，后续主客端都只播放实际状态边沿，不因快照重发重复发声。
            foreach (ShipState ship in State.Ships)
            {
                ShipFlightState flight = GetFlight(ship.ShipId);
                if (flight == null) continue;
                flightAudioStates.TryGetValue(ship.ShipId, out var previous);
                string cue = ResolveFlightAudioCue(flightAudioReady, previous.Ignited, previous.Landing,
                    flight.Ignited, flight.LandingEventId);
                flightAudioStates[ship.ShipId] = (flight.Ignited, flight.LandingEventId);
                if (cue != null && IsVisible(ship) && AudioService.HasInstance)
                    AudioService.Instance.Play(cue, AudioPlayOptions.At(DisplayPresentationPoint(ship, 0d, 0d),
                        flight.Phase == ShipFlightPhase.Cruising ? .1f : 1f));
            }
            expiredFlightAudioStates.Clear();
            foreach (string id in flightAudioStates.Keys)
                if (GetShip(id) == null) expiredFlightAudioStates.Add(id);
            foreach (string id in expiredFlightAudioStates) flightAudioStates.Remove(id);
            flightAudioReady = true;
        }

        internal static string ResolveFlightAudioCue(bool ready, bool wasIgnited, string previousLanding,
            bool ignited, string landing)
        {
            if (!ready) return null;
            if (!string.IsNullOrEmpty(landing) && landing != previousLanding) return "ship.landing";
            return !wasIgnited && ignited ? "ship.ignition" : null;
        }
        #endregion
    }
}
