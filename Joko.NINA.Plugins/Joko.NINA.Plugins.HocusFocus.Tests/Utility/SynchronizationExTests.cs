using System.Threading;
using System.Threading.Tasks;
using NINA.Joko.Plugins.HocusFocus.Utility;
using NUnit.Framework;

namespace NINA.Joko.Plugins.HocusFocus.Tests.Utility;

[TestFixture]
public class SynchronizationExTests {

    [Test]
    public void LockRead_AcquiresAndReleasesReadLock() {
        var rwLock = new ReaderWriterLockSlim();

        Assert.That(rwLock.IsReadLockHeld, Is.False);

        using (rwLock.LockRead()) {
            Assert.That(rwLock.IsReadLockHeld, Is.True);
        }

        Assert.That(rwLock.IsReadLockHeld, Is.False);
    }

    [Test]
    public void LockWrite_AcquiresAndReleasesWriteLock() {
        var rwLock = new ReaderWriterLockSlim();

        Assert.That(rwLock.IsWriteLockHeld, Is.False);

        using (rwLock.LockWrite()) {
            Assert.That(rwLock.IsWriteLockHeld, Is.True);
        }

        Assert.That(rwLock.IsWriteLockHeld, Is.False);
    }

    [Test]
    public void LockRead_AllowsConcurrentReaders() {
        var rwLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        var entered = new CountdownEvent(2);
        var release = new ManualResetEventSlim();

        var t1 = Task.Run(() => {
            using (rwLock.LockRead()) {
                entered.Signal();
                release.Wait();
            }
        });
        var t2 = Task.Run(() => {
            using (rwLock.LockRead()) {
                entered.Signal();
                release.Wait();
            }
        });

        Assert.That(entered.Wait(2000), Is.True, "Both readers should hold the lock concurrently");
        release.Set();
        Task.WaitAll(t1, t2);
    }

    [Test]
    public void LockWrite_BlocksUntilReaderReleases() {
        var rwLock = new ReaderWriterLockSlim();
        var readerEntered = new ManualResetEventSlim();
        var releaseReader = new ManualResetEventSlim();
        var writerAcquired = new ManualResetEventSlim();

        var reader = Task.Run(() => {
            using (rwLock.LockRead()) {
                readerEntered.Set();
                releaseReader.Wait();
            }
        });

        Assert.That(readerEntered.Wait(2000), Is.True);

        var writer = Task.Run(() => {
            using (rwLock.LockWrite()) {
                writerAcquired.Set();
            }
        });

        Assert.That(writerAcquired.Wait(200), Is.False, "Writer should be blocked while a reader holds the lock");

        releaseReader.Set();
        Assert.That(writerAcquired.Wait(2000), Is.True, "Writer should acquire after reader releases");
        Task.WaitAll(reader, writer);
    }
}
