﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.Extensions.Primitives
{
    /// <summary>
    /// An <see cref="IChangeToken"/> which represents one or more <see cref="IChangeToken"/> instances.
    /// </summary>
    public class CompositeChangeToken : IChangeToken
    {
        private static readonly Action<object> _onChangeDelegate = OnChange;
        private readonly object _callbackLock = new object();
        private CancellationTokenSource _cancellationTokenSource;
        private bool _registeredCallbackProxy;
        private List<IDisposable> _disposables;

        /// <summary>
        /// Creates a new instance of <see cref="CompositeChangeToken"/>.
        /// </summary>
        /// <param name="changeTokens">The list of <see cref="IChangeToken"/> to compose.</param>
        public CompositeChangeToken(IReadOnlyList<IChangeToken> changeTokens)
        {
            ChangeTokens = changeTokens ?? throw new ArgumentNullException(nameof(changeTokens));
            for (var i = 0; i < ChangeTokens.Count; i++)
            {
                if (ChangeTokens[i].ActiveChangeCallbacks)
                {
                    ActiveChangeCallbacks = true;
                    break;
                }
            }
        }

        /// <summary>
        /// Returns the list of <see cref="IChangeToken"/> which compose the current <see cref="CompositeChangeToken"/>.
        /// </summary>
        public IReadOnlyList<IChangeToken> ChangeTokens { get; }

        /// <inheritdoc />
        public IDisposable RegisterChangeCallback(Action<object> callback, object state)
        {
            EnsureCallbacksInitialized();
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                return _cancellationTokenSource.Token.Register(callback, state);
            }
            else
            {
                return NullDisposable.Singleton;
            }
        }

        /// <inheritdoc />
        public bool HasChanged
        {
            get
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    return true;
                }

                for (var i = 0; i < ChangeTokens.Count; i++)
                {
                    if (ChangeTokens[i].HasChanged)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <inheritdoc />
        public bool ActiveChangeCallbacks { get; }

        private void EnsureCallbacksInitialized()
        {
            if (_registeredCallbackProxy)
            {
                return;
            }

            lock (_callbackLock)
            {
                if (_registeredCallbackProxy)
                {
                    return;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                _disposables = new List<IDisposable>();
                for (var i = 0; i < ChangeTokens.Count; i++)
                {
                    if (ChangeTokens[i].ActiveChangeCallbacks)
                    {
                        var disposable = ChangeTokens[i].RegisterChangeCallback(_onChangeDelegate, this);
                        _disposables.Add(disposable);
                    }
                }
                _registeredCallbackProxy = true;
            }
        }

        private static void OnChange(object state)
        {
            var compositeChangeTokenState = (CompositeChangeToken)state;
            lock (compositeChangeTokenState._callbackLock)
            {
                try
                {
                    compositeChangeTokenState._cancellationTokenSource.Cancel();
                }
                catch
                {
                }
            }

            if (compositeChangeTokenState._disposables != null)
            {
                for (int i = 0; i < compositeChangeTokenState._disposables.Count; i++)
                {
                    compositeChangeTokenState._disposables[i].Dispose();
                }
            }
        }

        private class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Singleton = new NullDisposable();
            public bool Disposed { get; private set; }
            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
