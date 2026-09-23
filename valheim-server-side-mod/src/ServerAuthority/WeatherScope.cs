using UnityEngine;

namespace ServerAuthority
{
    /// <summary>
    /// Lends EnvMan the weather at one position for the length of one call, and gives the real one
    /// back afterwards.
    ///
    /// The consumers that read weather (WearNTear's rain wear and roof check, Fireplace's wet and
    /// wind check, Fire, Cinder, Windmill, the spawn lists, the sea under a hull) all read it the
    /// same way, through a handful of EnvMan statics and fields: IsWet, IsCold, IsFreezing,
    /// IsDaylight, GetCurrentEnvironment and everything built on it, and the wind. There were two
    /// ways to make them read a position's weather instead of the global one.
    ///
    /// One is to patch every call site: replace each consumer's method, or transpile each read of
    /// EnvMan.IsWet into a call that takes a position. That is a dozen methods, several of them
    /// large, and every one a copy of game code that has to be re-diffed after every update. A
    /// transpiler is worse: it matches an instruction pattern, and when Iron Gate moves the read
    /// it matches nothing, or the wrong thing, and says nothing.
    ///
    /// The other, used here, is to leave the consumers alone and swap what they read. A prefix on
    /// the consumer's entry method loads the position's weather into exactly the state EnvMan's
    /// own FixedUpdate would have left for a client standing there, and a postfix puts the global
    /// back. The consumers run vanilla code, byte for byte, including any new weather check a game
    /// update adds inside them. What can break is only the list of entry points, which PatchCheck
    /// verifies by name, or EnvMan renaming the fields swapped here, which fails the build rather
    /// than the behaviour. That is the same trade the rest of the mod makes by replacing methods
    /// rather than transpiling them.
    ///
    /// Scopes nest: each saves whatever it found, so an inner scope restores its outer one.
    /// Nothing is left behind when a consumer throws, because every patch that opens a scope also
    /// closes it in a finalizer.
    /// </summary>
    internal struct WeatherScope
    {
        private bool _weather;
        private bool _wind;
        private bool _water;

        private EnvSetup _env;
        private bool _wet;
        private bool _cold;
        private bool _freezing;
        private bool _daylight;

        private Vector4 _envWind;
        private Vector4 _envWind1;
        private Vector4 _envWind2;
        private float _envTimer;

        private Vector4 _water1;
        private Vector4 _water2;
        private float _waterAlpha;

        internal bool Active => _weather || _wind || _water;

        /// <summary>
        /// Whether the server should give its consumers the weather at their own position.
        /// </summary>
        internal static bool Enabled => Plugin.ServerActive && ModConfig.ServerWeatherPerPosition.Value;

        /// <summary>
        /// The weather at a position: wet, cold, freezing, daylight and the current environment.
        /// No wind, for consumers that do not read it.
        /// </summary>
        internal static WeatherScope Weather(Vector3 position)
        {
            WeatherScope scope = default;
            if (!Enabled)
            {
                return scope;
            }

            EnvMan env = EnvMan.instance;
            EnvSetup weather = LocalWeather.At(position);
            if (env == null || weather == null)
            {
                return scope;
            }

            scope.LoadWeather(env, weather);
            return scope;
        }

        /// <summary>
        /// The weather at a position and the wind that goes with it, for consumers that read wind
        /// through EnvMan (fires, cinders, windmills, fish) and for the sea under them.
        /// </summary>
        internal static WeatherScope WeatherAndWind(Vector3 position)
        {
            WeatherScope scope = default;
            if (!Enabled)
            {
                return scope;
            }

            EnvMan env = EnvMan.instance;
            EnvSetup weather = LocalWeather.At(position, out WeatherSource source);
            if (env == null || weather == null)
            {
                return scope;
            }

            scope.LoadWeather(env, weather);

            if (LocalWind.TryAt(weather, source, position, out Vector4 first, out Vector4 second, out float alpha))
            {
                scope.LoadWind(env, first, second, alpha);
            }

            return scope;
        }

        /// <summary>
        /// Only the wind WaterVolume builds its surface from, for WaterVolume.UpdateFloaters, which
        /// runs for every floating thing every frame and reads nothing else.
        /// </summary>
        internal static WeatherScope Water(Vector3 position)
        {
            WeatherScope scope = default;
            if (!Enabled)
            {
                return scope;
            }

            EnvMan env = EnvMan.instance;
            EnvSetup weather = LocalWeather.At(position, out WeatherSource source);
            if (env == null || weather == null)
            {
                return scope;
            }

            if (LocalWind.TryAt(weather, source, position, out Vector4 first, out Vector4 second, out float alpha))
            {
                scope.LoadWater(first, second, WaterAlpha(env, alpha));
            }

            return scope;
        }

        /// <summary>
        /// A ship's own weather and its own held wind anchors. See <see cref="LocalWind"/>.
        /// </summary>
        internal static WeatherScope Hull(Ship ship)
        {
            WeatherScope scope = default;
            if (!Enabled || ship == null)
            {
                return scope;
            }

            EnvMan env = EnvMan.instance;
            if (env == null)
            {
                return scope;
            }

            EnvSetup weather = LocalWeather.At(ship.transform.position);
            if (weather != null)
            {
                scope.LoadWeather(env, weather);
            }

            if (LocalWind.TryHull(ship, out Vector4 first, out Vector4 second, out float alpha))
            {
                scope.LoadWind(env, first, second, alpha);
            }

            return scope;
        }

        internal void Exit()
        {
            EnvMan env = EnvMan.instance;

            if (_weather && env != null)
            {
                env.m_currentEnv = _env;
                EnvMan.s_isWet = _wet;
                EnvMan.s_isCold = _cold;
                EnvMan.s_isFreezing = _freezing;
                EnvMan.s_isDaylight = _daylight;
            }

            if (_wind && env != null)
            {
                env.m_wind = _envWind;
                env.m_windDir1 = _envWind1;
                env.m_windDir2 = _envWind2;
                env.m_windTransitionTimer = _envTimer;
            }

            if (_water)
            {
                WaterVolume.s_globalWind1 = _water1;
                WaterVolume.s_globalWind2 = _water2;
                WaterVolume.s_globalWindAlpha = _waterAlpha;
            }

            _weather = false;
            _wind = false;
            _water = false;
        }

        /// <summary>
        /// The wind in the shape WaveField.Publish leaves it: both anchors, the blend expressed as
        /// the transition timer GetWindData divides back out, and the vector between them that
        /// GetWindDir and GetWindIntensity read. The sea gets the same three values through the
        /// same division, so that a hull here floats on exactly the surface a client computes
        /// from the same anchors.
        /// </summary>
        private void LoadWind(EnvMan env, Vector4 first, Vector4 second, float alpha)
        {
            _envWind = env.m_wind;
            _envWind1 = env.m_windDir1;
            _envWind2 = env.m_windDir2;
            _envTimer = env.m_windTransitionTimer;
            _wind = true;

            env.m_windDir1 = first;
            env.m_windDir2 = second;
            env.m_windTransitionTimer = alpha * env.m_windTransitionDuration;
            env.m_wind = Vector4.Lerp(first, second, alpha);

            env.GetWindData(out Vector4 wind1, out Vector4 wind2, out float blend);
            LoadWater(wind1, wind2, blend);
        }

        private void LoadWater(Vector4 first, Vector4 second, float alpha)
        {
            if (!_water)
            {
                _water1 = WaterVolume.s_globalWind1;
                _water2 = WaterVolume.s_globalWind2;
                _waterAlpha = WaterVolume.s_globalWindAlpha;
                _water = true;
            }

            WaterVolume.s_globalWind1 = first;
            WaterVolume.s_globalWind2 = second;
            WaterVolume.s_globalWindAlpha = alpha;
        }

        /// <summary>
        /// The blend exactly as EnvMan.GetWindData hands it to WaterVolume after WaveField.Publish
        /// stored it as a timer, so the float comes out bit for bit the same.
        /// </summary>
        private static float WaterAlpha(EnvMan env, float alpha)
        {
            float duration = env.m_windTransitionDuration;
            float timer = (float)(alpha * duration);
            return Mathf.Clamp01(timer / duration);
        }

        /// <summary>
        /// The state EnvMan.FixedUpdate derives from the current environment, recomputed for this
        /// one. IsDay is time of day alone and is the same everywhere, so it is read, not swapped.
        /// </summary>
        private void LoadWeather(EnvMan env, EnvSetup weather)
        {
            _env = env.m_currentEnv;
            _wet = EnvMan.s_isWet;
            _cold = EnvMan.s_isCold;
            _freezing = EnvMan.s_isFreezing;
            _daylight = EnvMan.s_isDaylight;
            _weather = true;

            bool day = EnvMan.IsDay();
            env.m_currentEnv = weather;
            EnvMan.s_isWet = weather.m_isWet;
            EnvMan.s_isCold = weather.m_isCold || (weather.m_isColdAtNight && !day);
            EnvMan.s_isFreezing = weather.m_isFreezing || (weather.m_isFreezingAtNight && !day);
            EnvMan.s_isDaylight = !weather.m_alwaysDark && day;
        }
    }
}
