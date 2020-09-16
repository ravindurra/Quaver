using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;
using Quaver.API.Enums;
using Quaver.API.Maps;
using Quaver.API.Maps.Structures;
using Quaver.Shared.Audio;
using System;
using System.Collections.Generic;
using System.Text;
using Wobble.Audio.Tracks;
using Wobble.Graphics;

namespace Quaver.Shared.Screens.Edit.Plugins
{
    [MoonSharpUserData]
    public class EditorPluginMap
    {
        [MoonSharpVisible(false)]
        public Qua Map;

        [MoonSharpVisible(false)]
        public IAudioTrack Track;

        /// <summary>
        ///     The game mode of the map
        /// </summary>
        public GameMode Mode { get; [MoonSharpVisible(false)] set; }

        /// <summary>
        ///     The slider velocities present in the map
        /// </summary>
        public List<SliderVelocityInfo> ScrollVelocities { get; [MoonSharpVisible(false)] set; }

        /// <summary>
        ///     The HitObjects that are currently in the map
        /// </summary>
        public List<HitObjectInfo> HitObjects { get; [MoonSharpVisible(false)] set; }

        /// <summary>
        ///     The timing points that are currently in the map
        /// </summary>
        public List<TimingPointInfo> TimingPoints { get; [MoonSharpVisible(false)] set; }

        /// <summary>
        ///     The editor layers that are currently in the map
        /// </summary>
        public List<EditorLayerInfo> EditorLayers { get; [MoonSharpVisible(false)] set; }

        /// <summary>
        ///     Total mp3 length
        /// </summary>
        public double TrackLength { get; [MoonSharpVisible(false)] set; }

        /// <summary>
        ///     The multiplier used before the first scroll velocity
        /// </summary>
        public float InitialScrollVelocity { get; [MoonSharpVisible(false)] set; }

        /// <summary>
        ///     Used to Round TrackPosition from Long to Float
        /// </summary>
        [MoonSharpVisible(false)]
        public static float TrackRounding { get; } = 100;

        [MoonSharpVisible(false)]
        public void SetFrameState()
        {
            Mode = Map.Mode;
            TimingPoints = Map.TimingPoints;
            ScrollVelocities = Map.SliderVelocities; // Original name was SliderVelocities but that name doesn't really make sense
            HitObjects = Map.HitObjects;
            EditorLayers = Map.EditorLayers;
            TrackLength = Track.Length;
            InitialScrollVelocity = Map.InitialScrollVelocity;
        }

        public override string ToString() => Map.ToString();

        /// <summary>
        ///    In Quaver, the key count is defined by the game mode.
        ///    This translates mode to key count.
        /// </summary>
        /// <returns></returns>
        public int GetKeyCount(bool includeScratch = true) => Map.GetKeyCount(includeScratch);

        /// <summary>
        ///     Finds the most common BPM in the current map
        /// </summary>
        /// <returns></returns>
        public float GetCommonBpm() => Map.GetCommonBpm();

        /// <summary>
        ///     Gets the timing point at a particular time in the current map.
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public TimingPointInfo GetTimingPointAt(double time) => Map.GetTimingPointAt(time);

        /// <summary>
        ///     Gets the scroll velocity at a particular time in the current map
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public SliderVelocityInfo GetScrollVelocityAt(double time) => Map.GetScrollVelocityAt(time);

        /// <summary>
        ///    Finds the length of a timing point.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public double GetTimingPointLength(TimingPointInfo point) => Map.GetTimingPointLength(point);

        /// <summary>
        ///     Gets the nearest snap time at a time to a given direction.
        /// </summary>
        /// <param name="forwards"></param>
        /// <param name="snap"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        public double GetNearestSnapTimeFromTime(bool forwards, int snap, float time) => AudioEngine.GetNearestSnapTimeFromTime(Map, forwards ? Direction.Forward : Direction.Backward, snap, time);

        /// <summary>
        ///     Gets the track position at a given time.
        /// </summary>
        /// <param name="time"></param>
        /// <returns></returns>
        public long GetPositionFromTime(double time)
        {
            if (time < Map.SliderVelocities[0].StartTime)
                return (long) (time * Map.InitialScrollVelocity * TrackRounding);

            var position = (long)(Map.SliderVelocities[0].StartTime * Map.InitialScrollVelocity * TrackRounding);
            
            int i;
            for (i = 1; i < Map.SliderVelocities.Count; i++)
            {
                if (time < Map.SliderVelocities[i].StartTime)
                    break;
                else
                    position += (long)((Map.SliderVelocities[i].StartTime - Map.SliderVelocities[i - 1].StartTime)
                                       * Map.SliderVelocities[i - 1].Multiplier * TrackRounding);
            }

            i--;

            position += (long)((time - Map.SliderVelocities[i].StartTime) * Map.SliderVelocities[i].Multiplier * TrackRounding);
            return position;
        }
    }
}
