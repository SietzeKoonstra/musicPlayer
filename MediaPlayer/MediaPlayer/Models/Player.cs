﻿using MediaPlayer;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using WPFSoundVisualizationLib;

namespace MusicPlayer
{
    class Player : ISpectrumPlayer, IWaveformPlayer, INotifyPropertyChanged
    {
        private Song currentSong { get; set; }
        private WaveOut musicPlayer;
        private WaveChannel32 inputStream;
        private WaveStream activeStream;
        public Songlist Songlist { get; set; }
        bool isPlaying = false;

        private bool disposed;

        private TimeSpan repeatStart;
        private TimeSpan repeatStop;
        private bool inRepeatSet;
        private Visualizer visualizer;
        private Visualizer waveFormVisualizer;
        private readonly int fftDataSize = (int)FFTDataSize.FFT2048;

        private bool inChannelSet;
        private float[] waveformData;
        private double channelLength;
        private double channelPosition;
        private bool inChannelTimerUpdate;
        private float[] fullLevelData;
        private string pendingWaveformPath;

        private readonly DispatcherTimer positionTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
        private readonly BackgroundWorker waveformGenerateWorker = new BackgroundWorker();

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler SongEnded;

        public Player()
        {
            this.currentSong = null;
            this.musicPlayer = new WaveOut();
            this.Songlist = new Songlist();
            positionTimer.Interval = TimeSpan.FromMilliseconds(50);
            positionTimer.Tick += positionTimer_Tick;
            

            waveformGenerateWorker.DoWork += waveformGenerateWorker_DoWork;
            waveformGenerateWorker.RunWorkerCompleted += waveformGenerateWorker_RunWorkerCompleted;
            waveformGenerateWorker.WorkerSupportsCancellation = true;
        }

        private void MusicPlayer_PlaybackStopped(object sender, EventArgs e)
        {
            SongEnded?.Invoke(this, e);
        }

        private static Player instance = null;
        private static readonly object padlock = new object();


        public static Player Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new Player();
                    }
                    return instance;
                }
            }
        }

        public Song CurrentSong
        {
            get { return this.currentSong; }
            set
            {
                string songLocation = value.SongLocation;
                if (File.Exists(songLocation))
                {
                    this.currentSong = value;
                    initializePlayerComponents();
                    
                }

            }
        }

        public void initializePlayerComponents()
        {
            try
            {
                if (IsPlaying)
                {
                    stop();
                }
                musicPlayer = new WaveOut()
                {
                    DesiredLatency = 100
                };
                this.musicPlayer.PlaybackStopped += MusicPlayer_PlaybackStopped;

                this.activeStream = new Mp3FileReader(this.currentSong.SongLocation);
                inputStream = new WaveChannel32(this.activeStream);

                this.inputStream.PadWithZeroes = false;

                this.visualizer = new Visualizer(fftDataSize);
                inputStream.Sample += inputStream_Sample;
                musicPlayer.Init(inputStream);
                ChannelLength = inputStream.TotalTime.TotalSeconds;
                GenerateWaveformData(this.currentSong.SongLocation);
            }
            catch
            {
                this.activeStream = null;
                isPlaying = false;
                Console.WriteLine("catched error");
            }
        }

        public bool IsPlaying
        {
            get { return isPlaying; }
            protected set
            {
                bool oldValue = isPlaying;
                isPlaying = value;
                if (oldValue != isPlaying)
                    NotifyPropertyChanged("IsPlaying");
                positionTimer.IsEnabled = value;
            }
        }

        internal void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    stop();
                }

                disposed = true;
            }
        }

        public double ChannelPosition
        {
            get { return channelPosition; }
            set
            {
                if (!inChannelSet)
                {
                    inChannelSet = true; // Avoid recursion
                    double oldValue = channelPosition;
                    double position = Math.Max(0, Math.Min(value, ChannelLength));
                    if (!inChannelTimerUpdate && this.activeStream != null)
                        this.activeStream.Position = (long)((position / this.activeStream.TotalTime.TotalSeconds) * this.activeStream.Length);
                    channelPosition = position;
                    if (oldValue != channelPosition)
                        NotifyPropertyChanged("ChannelPosition");
                    inChannelSet = false;
                }
            }
        }

        public double ChannelLength
        {
            get { return channelLength; }
            protected set
            {
                double oldValue = channelLength;
                channelLength = value;
                if (oldValue != channelLength)
                    NotifyPropertyChanged("ChannelLength");
            }
        }

        public float[] WaveformData
        {
            get { return waveformData; }
            protected set
            {
                float[] oldValue = waveformData;
                waveformData = value;
                if (oldValue != waveformData)
                    NotifyPropertyChanged("WaveformData");
            }
        }

        public TimeSpan SelectionBegin
        {
            get { return repeatStart; }
            set
            {
                if (!inRepeatSet)
                {
                    inRepeatSet = true;
                    TimeSpan oldValue = repeatStart;
                    repeatStart = value;
                    if (oldValue != repeatStart)
                        NotifyPropertyChanged("SelectionBegin");
                    inRepeatSet = false;
                }
            }
        }
        public TimeSpan SelectionEnd
        {
            get { return repeatStop; }
            set
            {
                if (!inChannelSet)
                {
                    inRepeatSet = true;
                    TimeSpan oldValue = repeatStop;
                    repeatStop = value;
                    if (oldValue != repeatStop)
                        NotifyPropertyChanged("SelectionEnd");
                    inRepeatSet = false;
                }
            }
        }

        private Boolean currentSongNotNull()
        {
            if (currentSong == null)
            {
                return false;
            }
            return true;
        }

        public void play()
        {
            
            if (CurrentSong == null)
            {
                if(this.Songlist.Count == 0)
                {
                    return;
                }
                
                Song newCurrentSong = this.Songlist.First();
                if (newCurrentSong != null)
                {
                    CurrentSong = newCurrentSong;
                }
                else
                {
                    return;
                }

            }
            this.musicPlayer.Play();
            this.IsPlaying = true;
            Console.WriteLine("playing");
            Console.ReadLine();
        }

        public void pause()
        {
            this.musicPlayer.Pause();
            this.IsPlaying = false;
        }

        public void stop()
        {
            if (musicPlayer != null)
            {
                musicPlayer.Stop();
            }
            if (this.activeStream != null)
            {
                inputStream.Close();
                inputStream = null;
                this.activeStream.Close();
                this.activeStream = null;
            }
            if (musicPlayer != null)
            {
                musicPlayer.Dispose();
                musicPlayer = null;
            }
            this.IsPlaying = false;
        }

        public Song getNextSong()
        {
            if (currentSongNotNull())
            {
                Song nextSong = this.Songlist.getNextSong(this.CurrentSong);
                if (nextSong != null)
                {
                    this.CurrentSong = nextSong;
                    return nextSong;
                }
            }
            return null;
        }

        public Song getPreviousSong()
        {
            if (currentSongNotNull())
            {
                Song previousSong = this.Songlist.getPreviousSong(this.CurrentSong);
                if (previousSong != null)
                {
                    this.CurrentSong = previousSong;
                    return previousSong;
                }
            }
            return null;
        }

        public bool GetFFTData(float[] fftDataBuffer)
        {
            visualizer.GetFFTResults(fftDataBuffer);
            return isPlaying;
        }

        public int GetFFTFrequencyIndex(int frequency)
        {
            double maxFrequency;
            if (this.activeStream != null)
                maxFrequency = this.activeStream.WaveFormat.SampleRate / 2.0d;
            else
                maxFrequency = 22050; // Assume a default 44.1 kHz sample rate.
            return (int)((frequency / maxFrequency) * (fftDataSize / 2));
        }

        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void waveformGenerateWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            WaveformGenerationParams waveformParams = e.Argument as WaveformGenerationParams;
            Mp3FileReader waveformMp3Stream = new Mp3FileReader(waveformParams.Path);
            WaveChannel32 waveformInputStream = new WaveChannel32(waveformMp3Stream);
            waveformInputStream.Sample += waveStream_Sample;

            int frameLength = fftDataSize;
            int frameCount = (int)((double)waveformInputStream.Length / (double)frameLength);
            int waveformLength = frameCount * 2;
            byte[] readBuffer = new byte[frameLength];
            waveFormVisualizer = new Visualizer(frameLength);

            float maxLeftPointLevel = float.MinValue;
            float maxRightPointLevel = float.MinValue;
            int currentPointIndex = 0;
            float[] waveformCompressedPoints = new float[waveformParams.Points];
            List<float> waveformData = new List<float>();
            List<int> waveMaxPointIndexes = new List<int>();

            for (int i = 1; i <= waveformParams.Points; i++)
            {
                waveMaxPointIndexes.Add((int)Math.Round(waveformLength * ((double)i / (double)waveformParams.Points), 0));
            }
            int readCount = 0;
            while (currentPointIndex * 2 < waveformParams.Points)
            {
                waveformInputStream.Read(readBuffer, 0, readBuffer.Length);

                waveformData.Add(waveFormVisualizer.LeftMaxVolume);
                waveformData.Add(waveFormVisualizer.RightMaxVolume);

                if (waveFormVisualizer.LeftMaxVolume > maxLeftPointLevel)
                    maxLeftPointLevel = waveFormVisualizer.LeftMaxVolume;
                if (waveFormVisualizer.RightMaxVolume > maxRightPointLevel)
                    maxRightPointLevel = waveFormVisualizer.RightMaxVolume;

                if (readCount > waveMaxPointIndexes[currentPointIndex])
                {
                    waveformCompressedPoints[(currentPointIndex * 2)] = maxLeftPointLevel;
                    waveformCompressedPoints[(currentPointIndex * 2) + 1] = maxRightPointLevel;
                    maxLeftPointLevel = float.MinValue;
                    maxRightPointLevel = float.MinValue;
                    currentPointIndex++;
                }
                if (readCount % 3000 == 0)
                {
                    float[] clonedData = (float[])waveformCompressedPoints.Clone();
                    App.Current.Dispatcher.Invoke(new Action(() =>
                    {
                        WaveformData = clonedData;
                    }));
                }

                if (waveformGenerateWorker.CancellationPending)
                {
                    e.Cancel = true;
                    break;
                }
                readCount++;
            }

            float[] finalClonedData = (float[])waveformCompressedPoints.Clone();
            App.Current.Dispatcher.Invoke(new Action(() =>
            {
                fullLevelData = waveformData.ToArray();
                WaveformData = finalClonedData;
            }));
            waveformInputStream.Close();
            waveformInputStream.Dispose();
            waveformInputStream = null;
            waveformMp3Stream.Close();
            waveformMp3Stream.Dispose();
            waveformMp3Stream = null;
        }

        private class WaveformGenerationParams
        {
            public WaveformGenerationParams(int points, string path)
            {
                Points = points;
                Path = path;
            }

            public int Points { get; protected set; }
            public string Path { get; protected set; }
        }

        private void waveStream_Sample(object sender, SampleEventArgs e)
        {
            waveFormVisualizer.Add(e.Left, e.Right);
        }

        private void positionTimer_Tick(object sender, EventArgs e)
        {
            inChannelTimerUpdate = true;
            ChannelPosition = ((double)this.activeStream.Position / (double)this.activeStream.Length) * this.activeStream.TotalTime.TotalSeconds;
            inChannelTimerUpdate = false;
        }

        private void GenerateWaveformData(string path)
        {
            if (waveformGenerateWorker.IsBusy)
            {
                pendingWaveformPath = path;
                waveformGenerateWorker.CancelAsync();
                return;
            }

            if (!waveformGenerateWorker.IsBusy && 2000 != 0)
                waveformGenerateWorker.RunWorkerAsync(new WaveformGenerationParams(2000, path));
        }

        private void waveformGenerateWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                if (!waveformGenerateWorker.IsBusy && 2000 != 0)
                    waveformGenerateWorker.RunWorkerAsync(new WaveformGenerationParams(2000, pendingWaveformPath));
            }
        }
        private void inputStream_Sample(object sender, SampleEventArgs e)
        {
            visualizer.Add(e.Left, e.Right);
            long repeatStartPosition = (long)((SelectionBegin.TotalSeconds / this.activeStream.TotalTime.TotalSeconds) * this.activeStream.Length);
            long repeatStopPosition = (long)((SelectionEnd.TotalSeconds / this.activeStream.TotalTime.TotalSeconds) * this.activeStream.Length);
            if (((SelectionEnd - SelectionBegin) >= TimeSpan.FromMilliseconds(200)) && this.activeStream.Position >= repeatStopPosition) // 200 = repeatthreshhold
            {
                visualizer.Clear();
                this.activeStream.Position = repeatStartPosition;
            }
        }
    }
}
