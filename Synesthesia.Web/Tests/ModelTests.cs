using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Synesthesia.Web.Data;
using Synesthesia.Web.Models;
using Xunit;

namespace Synesthesia.Web.Tests
{
    public class ModelTests
    {
        private ApplicationDbContext GetInMemoryDb()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
                .Options;
            return new ApplicationDbContext(options);
        }

        #region BaseEntity Tests

        [Fact]
        public void BaseEntity_Constructor_SetsCreatedAndUpdatedAt()
        {
            // Arrange & Act
            var entity = new AudioFile
            {
                UserId = "test",
                FileName = "test.mp3",
                FilePath = "/test.mp3",
                Format = "mp3"
            };

            // Assert
            Assert.NotNull(entity.CreatedAt);
            Assert.NotNull(entity.UpdatedAt);
            Assert.True(entity.CreatedAt <= DateTime.Now);
            Assert.True(entity.UpdatedAt <= DateTime.Now);
        }

        [Fact]
        public void BaseEntity_Update_ChangesUpdatedAt()
        {
            // Arrange
            var entity = new AudioFile
            {
                UserId = "test",
                FileName = "test.mp3",
                FilePath = "/test.mp3",
                Format = "mp3"
            };
            var originalUpdatedAt = entity.UpdatedAt;

            System.Threading.Thread.Sleep(10); // Ensure time passes

            // Act
            entity.Update();

            // Assert
            Assert.NotEqual(originalUpdatedAt, entity.UpdatedAt);
            Assert.True(entity.UpdatedAt > originalUpdatedAt);
        }

        [Fact]
        public void BaseEntity_GeneratesGuid()
        {
            // Arrange & Act
            var entity1 = new AudioFile
            {
                UserId = "test",
                FileName = "test1.mp3",
                FilePath = "/test1.mp3",
                Format = "mp3"
            };
            var entity2 = new AudioFile
            {
                UserId = "test",
                FileName = "test2.mp3",
                FilePath = "/test2.mp3",
                Format = "mp3"
            };

            // Assert
            Assert.NotEqual(Guid.Empty, entity1.Id);
            Assert.NotEqual(Guid.Empty, entity2.Id);
            Assert.NotEqual(entity1.Id, entity2.Id);
        }

        #endregion

        #region AudioFile Tests

        [Fact]
        public async Task AudioFile_CanBeSavedToDatabase()
        {
            // Arrange
            var db = GetInMemoryDb();
            var audioFile = new AudioFile
            {
                UserId = "user-123",
                FileName = "song.mp3",
                FilePath = "/uploads/audio/song.mp3",
                Format = "mp3"
            };

            // Act
            await db.AudioFiles.AddAsync(audioFile);
            await db.SaveChangesAsync();

            // Assert
            var saved = await db.AudioFiles.FirstOrDefaultAsync();
            Assert.NotNull(saved);
            Assert.Equal("song.mp3", saved.FileName);
            Assert.Equal("user-123", saved.UserId);
        }

        [Fact]
        public async Task AudioFile_CanHaveMultipleFractalProjects()
        {
            // Arrange
            var db = GetInMemoryDb();
            var audioFile = new AudioFile
            {
                UserId = "user-123",
                FileName = "song.mp3",
                FilePath = "/uploads/audio/song.mp3",
                Format = "mp3"
            };

            var project1 = new FractalProject
            {
                UserId = "user-123",
                AudioId = audioFile.Id,
                Title = "Project 1",
                FractalType = "julia",
                SettingsJson = "{}"
            };

            var project2 = new FractalProject
            {
                UserId = "user-123",
                AudioId = audioFile.Id,
                Title = "Project 2",
                FractalType = "mandelbrot",
                SettingsJson = "{}"
            };

            // Act
            await db.AudioFiles.AddAsync(audioFile);
            await db.FractalProjects.AddRangeAsync(project1, project2);
            await db.SaveChangesAsync();

            // Assert
            var loadedAudio = await db.AudioFiles
                .Include(a => a.FractalProjects)
                .FirstAsync();
            Assert.Equal(2, loadedAudio.FractalProjects!.Count);
        }

        #endregion

        #region FractalProject Tests

        [Fact]
        public async Task FractalProject_CanBeSavedToDatabase()
        {
            // Arrange
            var db = GetInMemoryDb();
            var audioFile = new AudioFile
            {
                UserId = "user-123",
                FileName = "song.mp3",
                FilePath = "/uploads/audio/song.mp3",
                Format = "mp3"
            };

            var project = new FractalProject
            {
                UserId = "user-123",
                AudioId = audioFile.Id,
                Title = "My Fractal",
                FractalType = "julia",
                SettingsJson = "{\"iterations\": 300}"
            };

            // Act
            await db.AudioFiles.AddAsync(audioFile);
            await db.FractalProjects.AddAsync(project);
            await db.SaveChangesAsync();

            // Assert
            var saved = await db.FractalProjects
                .Include(p => p.AudioFile)
                .FirstAsync();
            Assert.NotNull(saved);
            Assert.Equal("My Fractal", saved.Title);
            Assert.Equal("julia", saved.FractalType);
            Assert.NotNull(saved.AudioFile);
            Assert.Equal("song.mp3", saved.AudioFile.FileName);
        }

        [Fact]
        public async Task FractalProject_DeleteRestrict_PreventsDeletingAudioWithProjects()
        {
            // Arrange
            var db = GetInMemoryDb();
            var audioFile = new AudioFile
            {
                UserId = "user-123",
                FileName = "song.mp3",
                FilePath = "/uploads/audio/song.mp3",
                Format = "mp3"
            };

            var project = new FractalProject
            {
                UserId = "user-123",
                AudioId = audioFile.Id,
                Title = "My Fractal",
                FractalType = "julia",
                SettingsJson = "{}"
            };

            await db.AudioFiles.AddAsync(audioFile);
            await db.FractalProjects.AddAsync(project);
            await db.SaveChangesAsync();

            // Act & Assert
            db.AudioFiles.Remove(audioFile);
            await Assert.ThrowsAsync<DbUpdateException>(async () =>
                await db.SaveChangesAsync());
        }

        [Fact]
        public async Task FractalProject_ValidSettingsJson_CanBeStored()
        {
            // Arrange
            var db = GetInMemoryDb();
            var audioFile = new AudioFile
            {
                UserId = "user-123",
                FileName = "song.mp3",
                FilePath = "/uploads/audio/song.mp3",
                Format = "mp3"
            };

            var settingsJson = @"{
                ""fractalType"": ""julia"",
                ""iterations"": 300,
                ""bassStrength"": 1.5,
                ""trebleStrength"": 2.0,
                ""primaryColor"": ""#291E7D"",
                ""secondaryColor"": ""#872D53"",
                ""rainbow"": false
            }";

            var project = new FractalProject
            {
                UserId = "user-123",
                AudioId = audioFile.Id,
                Title = "Complex Project",
                FractalType = "julia",
                SettingsJson = settingsJson
            };

            // Act
            await db.AudioFiles.AddAsync(audioFile);
            await db.FractalProjects.AddAsync(project);
            await db.SaveChangesAsync();

            // Assert
            var saved = await db.FractalProjects.FirstAsync();
            Assert.Contains("iterations", saved.SettingsJson);
            Assert.Contains("300", saved.SettingsJson);
        }

        #endregion

        #region SavedVideo Tests

        [Fact]
        public async Task SavedVideo_CanBeSavedToDatabase()
        {
            // Arrange
            var db = GetInMemoryDb();
            var audioFile = new AudioFile
            {
                UserId = "user-123",
                FileName = "song.mp3",
                FilePath = "/uploads/audio/song.mp3",
                Format = "mp3"
            };

            var video = new SavedVideo
            {
                UserId = "user-123",
                AudioId = audioFile.Id,
                VideoPath = "/uploads/videos/video1.webm",
                Title = "My Video"
            };

            // Act
            await db.AudioFiles.AddAsync(audioFile);
            await db.SavedVideos.AddAsync(video);
            await db.SaveChangesAsync();

            // Assert
            var saved = await db.SavedVideos
                .Include(v => v.AudioFile)
                .FirstAsync();
            Assert.NotNull(saved);
            Assert.Equal("My Video", saved.Title);
            Assert.NotNull(saved.AudioFile);
        }

        #endregion

        #region AppUser Tests

        [Fact]
        public async Task AppUser_CanHaveMultipleAudioFiles()
        {
            // Arrange
            var db = GetInMemoryDb();
            var user = new AppUser
            {
                Id = "user-123",
                UserName = "testuser",
                Email = "test@test.com"
            };

            var audio1 = new AudioFile
            {
                UserId = user.Id,
                FileName = "song1.mp3",
                FilePath = "/uploads/audio/song1.mp3",
                Format = "mp3"
            };

            var audio2 = new AudioFile
            {
                UserId = user.Id,
                FileName = "song2.mp3",
                FilePath = "/uploads/audio/song2.mp3",
                Format = "mp3"
            };

            // Act
            await db.Users.AddAsync(user);
            await db.AudioFiles.AddRangeAsync(audio1, audio2);
            await db.SaveChangesAsync();

            // Assert
            var audioCount = await db.AudioFiles
                .Where(a => a.UserId == user.Id)
                .CountAsync();
            Assert.Equal(2, audioCount);
        }

        [Fact]
        public async Task AppUser_ProfilePictureAndBio_CanBeSet()
        {
            // Arrange
            var db = GetInMemoryDb();
            var user = new AppUser
            {
                Id = "user-123",
                UserName = "testuser",
                Email = "test@test.com",
                ProfilePicture = "/uploads/profiles/pic.jpg",
                Bio = "I love fractals!"
            };

            // Act
            await db.Users.AddAsync(user);
            await db.SaveChangesAsync();

            // Assert
            var saved = await db.Users.FirstAsync();
            Assert.Equal("/uploads/profiles/pic.jpg", saved.ProfilePicture);
            Assert.Equal("I love fractals!", saved.Bio);
        }

        #endregion

        #region Database Constraint Tests

        [Fact]
        public async Task Database_CascadeDelete_UserDeletesTheirFiles()
        {
            // Arrange
            var db = GetInMemoryDb();
            var user = new AppUser
            {
                Id = "user-123",
                UserName = "testuser",
                Email = "test@test.com"
            };

            var audioFile = new AudioFile
            {
                UserId = user.Id,
                FileName = "song.mp3",
                FilePath = "/uploads/audio/song.mp3",
                Format = "mp3"
            };

            await db.Users.AddAsync(user);
            await db.AudioFiles.AddAsync(audioFile);
            await db.SaveChangesAsync();

            // Act
            db.Users.Remove(user);
            await db.SaveChangesAsync();

            // Assert
            var audioCount = await db.AudioFiles.CountAsync();
            Assert.Equal(0, audioCount);
        }

        #endregion
    }
}