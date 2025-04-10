using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;

public class TimelineManager : MonoBehaviour
{
    public enum BindingStreamType
    {
        GenericBinding,
        ReferenceValue
    }

    public struct Origin
    {
        public bool hasParent;
        public Vector3 scale;
        public Vector3 position;
        public Quaternion rotation;
        public Transform parent;
        public Transform instance;

        public Origin(Transform instance)
        {
            this.instance = instance;
            parent = instance.parent;
            rotation = instance.localRotation;
            position = instance.localPosition;
            scale = instance.localScale;
            hasParent = parent != null;
        }

        public void Playback()
        {
            if (hasParent && parent == null)
                Destroy(instance.gameObject);
            else
            {
                instance.localScale = scale;
                instance.localPosition = position;
                instance.localRotation = rotation;
                instance.SetParent(parent, false);
            }
        }
    }

    public struct BindingStream : IEquatable<BindingStream>
    {
        public BindingStreamType type;
        public bool isInvert;
        public string name;
        public string path;
        public string targetParentPath;
        public UnityEngine.Object target;
        public Transform root;

        public bool Equals(BindingStream other)
        {
            return type == other.type &&
                   isInvert == other.isInvert &&
                   name == other.name &&
                   path == other.path &&
                   targetParentPath == other.targetParentPath &&
                   target == other.target && 
                   root == other.root;
        }
    }

    public PlayableDirector[] playableDirectors;

    private PlayableDirector __playableDirector;

    public static List<Origin> Bind(PlayableDirector playableDirector, IReadOnlyCollection<BindingStream> bindingStreams)
    {
        Transform transform = playableDirector.transform, temp;
        List<Origin> origins = null;
        if (bindingStreams != null && bindingStreams.Count > 0)
        {
            var playableAsset = playableDirector.playableAsset;
            var outputs = playableAsset == null ? null : playableAsset.outputs;

            UnityEngine.Object target;
            Component component;
            GameObject gameObject;
            foreach (var bindingStream in bindingStreams)
            {
                temp = bindingStream.root == null ? transform : bindingStream.root;
                if (!string.IsNullOrEmpty(bindingStream.path))
                {
                    gameObject = bindingStream.target as GameObject;
                    if (gameObject == null)
                        gameObject = ((Component)bindingStream.target).gameObject;

                    if (origins == null)
                        origins = new List<Origin>();

                    if (bindingStream.isInvert)
                    {
                        origins.Add(new Origin(temp));

                        temp.SetParent(
                            bindingStream.path == "/"
                                ? gameObject.transform
                                : gameObject.transform.Find(bindingStream.path), false);
                    }
                    else
                    {
                        origins.Add(new Origin(gameObject.transform));

                        gameObject.transform.SetParent(bindingStream.path == "/" ? temp : temp.Find(bindingStream.path), false);
                    }
                }

                if (bindingStream.target == null && !string.IsNullOrEmpty(bindingStream.targetParentPath))
                {
                    component = temp.GetComponent(bindingStream.targetParentPath);
                    while (component == null)
                    {
                        temp = temp.parent;
                        if (temp == null)
                            break;

                        component = temp.GetComponent(bindingStream.targetParentPath);
                    }

                    target = component;
                }
                else
                    target = bindingStream.target;

                switch (bindingStream.type)
                {
                    case BindingStreamType.GenericBinding:
                        if (outputs != null)
                        {
                            foreach (var output in outputs)
                            {
                                if (output.streamName == bindingStream.name)
                                {
                                    playableDirector.SetGenericBinding(output.sourceObject, target);

                                    break;
                                }
                            }
                        }
                        break;
                    case BindingStreamType.ReferenceValue:
                        playableDirector.SetReferenceValue(bindingStream.name, target);
                        break;
                }
            }
        }

        return origins;
    }

    public PlayableDirector Find(string name)
    {
        foreach(var playableDirector in playableDirectors)
        {
            if (playableDirector.name == name)
                return playableDirector;
        }

        return null;
    }

    public bool Play(
        PlayableDirector playableDirector, 
        Action onPaused, 
        Action onStopped, 
        IReadOnlyCollection<BindingStream> bindingStreams)
    {
        if (playableDirector == null)
            return false;

        if(__playableDirector != null)
            __playableDirector.Stop();

        __playableDirector = playableDirector;

        var origins = Bind(playableDirector, bindingStreams);

        Action<PlayableDirector> paused = null;
        if (onPaused != null)
        {
            paused = x =>
            {
                if (x != null)
                    x.paused -= paused;

                onPaused();
            };

            playableDirector.paused += paused;
        }

        Action<PlayableDirector> stopped = null;
        stopped = x =>
        {
            if (x == __playableDirector)
                __playableDirector = null;

            if (x != null)
            {
                x.stopped -= stopped;

                if(paused != null)
                    x.paused -= paused;
            }

            //transform.SetParent(parent, false);

            if (origins != null)
            {
                foreach (var origin in origins)
                    origin.Playback();
            }

            if(onStopped != null)
                onStopped();
        };

        playableDirector.stopped += stopped;

        playableDirector.Play();

        return true;
    }

    public PlayableDirector Play(string name, Action onPaused, Action onStopped, IReadOnlyCollection<BindingStream> bindingStreams)
    {
        var playableDirector = Find(name);
        return Play(playableDirector, onPaused, onStopped, bindingStreams) ? playableDirector : null;
    }

    public bool Resume(PlayableDirector playableDirector, Action onPaused, Action onStopped)
    {
        if (playableDirector == null && playableDirector.state != PlayState.Paused)
            return false;

        Action<PlayableDirector> paused = null;
        if (onPaused != null)
        {
            paused = x =>
            {
                if (x != null)
                    x.paused -= paused;

                onPaused();
            };

            playableDirector.paused += paused;
        }


        if (onStopped != null)
        {
            Action<PlayableDirector> stopped = null;
            stopped = x =>
            {
                if (x != null)
                {
                    if (paused != null)
                        x.paused -= paused;

                    x.stopped -= stopped;
                }

                onStopped();
            };

            playableDirector.stopped += stopped;
        }

        playableDirector.Resume();

        return true;
    }

    public bool Resume(string name, Action onPaused, Action onStopped)
    {
        return Resume(Find(name), onPaused, onStopped);
    }

    public bool StopAll()
    {
        if (__playableDirector == null)
            return false;

        __playableDirector.Stop();
        __playableDirector = null;

        return true;
    }
}
