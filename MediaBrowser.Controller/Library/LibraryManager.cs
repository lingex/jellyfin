﻿using MediaBrowser.Common.Events;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Common.IO;
using MediaBrowser.Common.Kernel;
using MediaBrowser.Common.Win32;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.IO;
using MediaBrowser.Controller.Resolvers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using MoreLinq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Controller.Library
{
    /// <summary>
    /// Class LibraryManager
    /// </summary>
    public class LibraryManager : BaseManager<Kernel>
    {
        #region LibraryChanged Event
        /// <summary>
        /// Fires whenever any validation routine adds or removes items.  The added and removed items are properties of the args.
        /// *** Will fire asynchronously. ***
        /// </summary>
        public event EventHandler<ChildrenChangedEventArgs> LibraryChanged;

        /// <summary>
        /// Raises the <see cref="E:LibraryChanged" /> event.
        /// </summary>
        /// <param name="args">The <see cref="ChildrenChangedEventArgs" /> instance containing the event data.</param>
        internal void OnLibraryChanged(ChildrenChangedEventArgs args)
        {
            EventHelper.QueueEventIfNotNull(LibraryChanged, this, args, _logger);

            // Had to put this in a separate method to avoid an implicitly captured closure
            SendLibraryChangedWebSocketMessage(args);
        }

        /// <summary>
        /// Sends the library changed web socket message.
        /// </summary>
        /// <param name="args">The <see cref="ChildrenChangedEventArgs" /> instance containing the event data.</param>
        private void SendLibraryChangedWebSocketMessage(ChildrenChangedEventArgs args)
        {
            // Notify connected ui's
            Kernel.TcpManager.SendWebSocketMessage("LibraryChanged", () => DtoBuilder.GetLibraryUpdateInfo(args));
        }
        #endregion

        /// <summary>
        /// The _logger
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryManager" /> class.
        /// </summary>
        /// <param name="kernel">The kernel.</param>
        /// <param name="logger">The logger.</param>
        public LibraryManager(Kernel kernel, ILogger logger)
            : base(kernel)
        {
            _logger = logger;
        }

        /// <summary>
        /// Resolves the item.
        /// </summary>
        /// <param name="args">The args.</param>
        /// <returns>BaseItem.</returns>
        public BaseItem ResolveItem(ItemResolveArgs args)
        {
            return Kernel.EntityResolvers.Select(r => r.ResolvePath(args)).FirstOrDefault(i => i != null);
        }

        /// <summary>
        /// Resolves a path into a BaseItem
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="fileInfo">The file info.</param>
        /// <returns>BaseItem.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        public BaseItem GetItem(string path, Folder parent = null, WIN32_FIND_DATA? fileInfo = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException();
            }

            fileInfo = fileInfo ?? FileSystem.GetFileData(path);

            if (!fileInfo.HasValue)
            {
                return null;
            }

            var args = new ItemResolveArgs
            {
                Parent = parent,
                Path = path,
                FileInfo = fileInfo.Value
            };

            // Return null if ignore rules deem that we should do so
            if (Kernel.EntityResolutionIgnoreRules.Any(r => r.ShouldIgnore(args)))
            {
                return null;
            }

            // Gather child folder and files
            if (args.IsDirectory)
            {
                // When resolving the root, we need it's grandchildren (children of user views)
                var flattenFolderDepth = args.IsPhysicalRoot ? 2 : 0;

                args.FileSystemDictionary = FileData.GetFilteredFileSystemEntries(args.Path, _logger, flattenFolderDepth: flattenFolderDepth, args: args);
            }

            // Check to see if we should resolve based on our contents
            if (args.IsDirectory && !EntityResolutionHelper.ShouldResolvePathContents(args))
            {
                return null;
            }

            return ResolveItem(args);
        }

        /// <summary>
        /// Resolves a set of files into a list of BaseItem
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="files">The files.</param>
        /// <param name="parent">The parent.</param>
        /// <returns>List{``0}.</returns>
        public List<T> GetItems<T>(IEnumerable<WIN32_FIND_DATA> files, Folder parent)
            where T : BaseItem
        {
            var list = new List<T>();

            Parallel.ForEach(files, f =>
            {
                try
                {
                    var item = GetItem(f.Path, parent, f) as T;

                    if (item != null)
                    {
                        lock (list)
                        {
                            list.Add(item);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error resolving path {0}", ex, f.Path);
                }
            });

            return list;
        }

        /// <summary>
        /// Creates the root media folder
        /// </summary>
        /// <returns>AggregateFolder.</returns>
        /// <exception cref="System.InvalidOperationException">Cannot create the root folder until plugins have loaded</exception>
        internal AggregateFolder CreateRootFolder()
        {
            if (Kernel.Plugins == null)
            {
                throw new InvalidOperationException("Cannot create the root folder until plugins have loaded");
            }

            var rootFolderPath = Kernel.ApplicationPaths.RootFolderPath;
            var rootFolder = Kernel.ItemRepository.RetrieveItem(rootFolderPath.GetMBId(typeof(AggregateFolder))) as AggregateFolder ?? (AggregateFolder)GetItem(rootFolderPath);

            // Add in the plug-in folders
            foreach (var child in Kernel.PluginFolders)
            {
                rootFolder.AddVirtualChild(child);
            }

            return rootFolder;
        }

        /// <summary>
        /// Gets a Person
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="allowSlowProviders">if set to <c>true</c> [allow slow providers].</param>
        /// <returns>Task{Person}.</returns>
        public Task<Person> GetPerson(string name, bool allowSlowProviders = false)
        {
            return GetPerson(name, CancellationToken.None, allowSlowProviders);
        }

        /// <summary>
        /// Gets a Person
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="allowSlowProviders">if set to <c>true</c> [allow slow providers].</param>
        /// <returns>Task{Person}.</returns>
        private Task<Person> GetPerson(string name, CancellationToken cancellationToken, bool allowSlowProviders = false)
        {
            return GetImagesByNameItem<Person>(Kernel.ApplicationPaths.PeoplePath, name, cancellationToken, allowSlowProviders);
        }

        /// <summary>
        /// Gets a Studio
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="allowSlowProviders">if set to <c>true</c> [allow slow providers].</param>
        /// <returns>Task{Studio}.</returns>
        public Task<Studio> GetStudio(string name, bool allowSlowProviders = false)
        {
            return GetImagesByNameItem<Studio>(Kernel.ApplicationPaths.StudioPath, name, CancellationToken.None, allowSlowProviders);
        }

        /// <summary>
        /// Gets a Genre
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="allowSlowProviders">if set to <c>true</c> [allow slow providers].</param>
        /// <returns>Task{Genre}.</returns>
        public Task<Genre> GetGenre(string name, bool allowSlowProviders = false)
        {
            return GetImagesByNameItem<Genre>(Kernel.ApplicationPaths.GenrePath, name, CancellationToken.None, allowSlowProviders);
        }

        /// <summary>
        /// The us culture
        /// </summary>
        private static readonly CultureInfo UsCulture = new CultureInfo("en-US");

        /// <summary>
        /// Gets a Year
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="allowSlowProviders">if set to <c>true</c> [allow slow providers].</param>
        /// <returns>Task{Year}.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException"></exception>
        public Task<Year> GetYear(int value, bool allowSlowProviders = false)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            return GetImagesByNameItem<Year>(Kernel.ApplicationPaths.YearPath, value.ToString(UsCulture), CancellationToken.None, allowSlowProviders);
        }

        /// <summary>
        /// The images by name item cache
        /// </summary>
        private readonly ConcurrentDictionary<string, object> ImagesByNameItemCache = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Generically retrieves an IBN item
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path">The path.</param>
        /// <param name="name">The name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="allowSlowProviders">if set to <c>true</c> [allow slow providers].</param>
        /// <returns>Task{``0}.</returns>
        /// <exception cref="System.ArgumentNullException"></exception>
        private Task<T> GetImagesByNameItem<T>(string path, string name, CancellationToken cancellationToken, bool allowSlowProviders = true)
            where T : BaseItem, new()
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException();
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException();
            }

            var key = Path.Combine(path, FileSystem.GetValidFilename(name));

            var obj = ImagesByNameItemCache.GetOrAdd(key, keyname => CreateImagesByNameItem<T>(path, name, cancellationToken, allowSlowProviders));

            return obj as Task<T>;
        }

        /// <summary>
        /// Creates an IBN item based on a given path
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path">The path.</param>
        /// <param name="name">The name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="allowSlowProviders">if set to <c>true</c> [allow slow providers].</param>
        /// <returns>Task{``0}.</returns>
        /// <exception cref="System.IO.IOException">Path not created:  + path</exception>
        private async Task<T> CreateImagesByNameItem<T>(string path, string name, CancellationToken cancellationToken, bool allowSlowProviders = true)
            where T : BaseItem, new()
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.Debug("Creating {0}: {1}", typeof(T).Name, name);

            path = Path.Combine(path, FileSystem.GetValidFilename(name));

            var fileInfo = FileSystem.GetFileData(path);

            var isNew = false;

            if (!fileInfo.HasValue)
            {
                Directory.CreateDirectory(path);
                fileInfo = FileSystem.GetFileData(path);

                if (!fileInfo.HasValue)
                {
                    throw new IOException("Path not created: " + path);
                }

                isNew = true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var id = path.GetMBId(typeof(T));

            var item = Kernel.ItemRepository.RetrieveItem(id) as T;
            if (item == null)
            {
                item = new T
                {
                    Name = name,
                    Id = id,
                    DateCreated = fileInfo.Value.CreationTimeUtc,
                    DateModified = fileInfo.Value.LastWriteTimeUtc,
                    Path = path
                };
                isNew = true;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Set this now so we don't cause additional file system access during provider executions
            item.ResetResolveArgs(fileInfo);

            await item.RefreshMetadata(cancellationToken, isNew, allowSlowProviders: allowSlowProviders).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            return item;
        }

        /// <summary>
        /// Validate and refresh the People sub-set of the IBN.
        /// The items are stored in the db but not loaded into memory until actually requested by an operation.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="progress">The progress.</param>
        /// <returns>Task.</returns>
        internal async Task ValidatePeople(CancellationToken cancellationToken, IProgress<TaskProgress> progress)
        {
            // Clear the IBN cache
            ImagesByNameItemCache.Clear();

            const int maxTasks = 250;

            var tasks = new List<Task>();

            var includedPersonTypes = new[] { PersonType.Actor, PersonType.Director };

            var people = Kernel.RootFolder.RecursiveChildren
                .Where(c => c.People != null)
                .SelectMany(c => c.People.Where(p => includedPersonTypes.Contains(p.Type)))
                .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var numComplete = 0;

            foreach (var person in people)
            {
                if (tasks.Count > maxTasks)
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    tasks.Clear();

                    // Safe cancellation point, when there are no pending tasks
                    cancellationToken.ThrowIfCancellationRequested();
                }

                // Avoid accessing the foreach variable within the closure
                var currentPerson = person;

                tasks.Add(Task.Run(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        await GetPerson(currentPerson.Name, cancellationToken, allowSlowProviders: true).ConfigureAwait(false);
                    }
                    catch (IOException ex)
                    {
                        _logger.ErrorException("Error validating IBN entry {0}", ex, currentPerson.Name);
                    }

                    // Update progress
                    lock (progress)
                    {
                        numComplete++;
                        double percent = numComplete;
                        percent /= people.Count;

                        progress.Report(new TaskProgress { PercentComplete = 100 * percent });
                    }
                }));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);

            progress.Report(new TaskProgress { PercentComplete = 100 });

            _logger.Info("People validation complete");
        }

        /// <summary>
        /// Reloads the root media folder
        /// </summary>
        /// <param name="progress">The progress.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Task.</returns>
        internal async Task ValidateMediaLibrary(IProgress<TaskProgress> progress, CancellationToken cancellationToken)
        {
            _logger.Info("Validating media library");

            await Kernel.RootFolder.RefreshMetadata(cancellationToken).ConfigureAwait(false);

            // Start by just validating the children of the root, but go no further
            await Kernel.RootFolder.ValidateChildren(new Progress<TaskProgress> { }, cancellationToken, recursive: false);

            // Validate only the collection folders for each user, just to make them available as quickly as possible
            var userCollectionFolderTasks = Kernel.Users.AsParallel().Select(user => user.ValidateCollectionFolders(new Progress<TaskProgress> { }, cancellationToken));
            await Task.WhenAll(userCollectionFolderTasks).ConfigureAwait(false);

            // Now validate the entire media library
            await Kernel.RootFolder.ValidateChildren(progress, cancellationToken, recursive: true).ConfigureAwait(false);

            foreach (var user in Kernel.Users)
            {
                await user.ValidateMediaLibrary(new Progress<TaskProgress> { }, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Saves display preferences for a Folder
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="folder">The folder.</param>
        /// <param name="data">The data.</param>
        /// <returns>Task.</returns>
        public Task SaveDisplayPreferencesForFolder(User user, Folder folder, DisplayPreferences data)
        {
            // Need to update all items with the same DisplayPrefsId
            foreach (var child in Kernel.RootFolder.GetRecursiveChildren(user)
                .OfType<Folder>()
                .Where(i => i.DisplayPrefsId == folder.DisplayPrefsId))
            {
                child.AddOrUpdateDisplayPrefs(user, data);
            }

            return Kernel.DisplayPreferencesRepository.SaveDisplayPrefs(folder, CancellationToken.None);
        }

        /// <summary>
        /// Gets the default view.
        /// </summary>
        /// <returns>IEnumerable{VirtualFolderInfo}.</returns>
        public IEnumerable<VirtualFolderInfo> GetDefaultVirtualFolders()
        {
            return GetView(Kernel.ApplicationPaths.DefaultUserViewsPath);
        }

        /// <summary>
        /// Gets the view.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <returns>IEnumerable{VirtualFolderInfo}.</returns>
        public IEnumerable<VirtualFolderInfo> GetVirtualFolders(User user)
        {
            return GetView(user.RootFolderPath);
        }

        /// <summary>
        /// Gets the view.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>IEnumerable{VirtualFolderInfo}.</returns>
        private IEnumerable<VirtualFolderInfo> GetView(string path)
        {
            return Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly)
                .Select(dir => new VirtualFolderInfo
                {
                    Name = Path.GetFileName(dir),
                    Locations = Directory.EnumerateFiles(dir, "*.lnk", SearchOption.TopDirectoryOnly).Select(FileSystem.ResolveShortcut).ToList()
                });
        }
    }
}
