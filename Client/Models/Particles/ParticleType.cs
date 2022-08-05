﻿using Client.Envir;
using SlimDX;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.Models.Particles
{
    public abstract class ParticleType : IDisposable
    {
        public MirLibrary Library;

        public List<Particle> Particles = new();

        public int MaxCount { get; set; }
        public TimeSpan SpawnFrequency { get; set; }
        public DateTime NextSpawn { get; set; }

        public Color Color;

        public List<int> Textures;
        public Random random = new();

        public abstract Particle CreateParticle(Vector2 emitterLocation, int direction, float angle);

        #region IDisposable
        public bool IsDisposed { get; private set; }
        public void Dispose()
        {
            Dispose(!IsDisposed);
            GC.SuppressFinalize(this);
        }

        protected void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;

                Library = null;
                Particles = null;
                Textures = null;
            }
        }

        #endregion
    }
}