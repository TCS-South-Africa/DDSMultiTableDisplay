using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace MTD.Tests
{
    // How the T-Connect client chooses between table lists that reach it out of order. The Key-Value watch and the
    // 30-second direct read run on different threads and can deliver the same entry, or different revisions of it,
    // in either order. No server is needed: the entries are handed to the client the way those two paths do.
    public class TConnectTableListTests
    {
        MainThreadQueue queue;
        TableIdListSource tableList;
        TConnectMtdClient client;

        static readonly Guid tableA = TestSamples.TableId(1);
        static readonly Guid tableB = TestSamples.TableId(2);
        static readonly Guid tableC = TestSamples.TableId(3);

        [SetUp]
        public void SetUp()
        {
            queue = new MainThreadQueue();
            tableList = new TableIdListSource();
            // The transport is never started; the client only needs one to exist.
            client = new TConnectMtdClient(new NatsMtdTransport(new MtdConnectionSettings()), Guid.NewGuid(), "MTD 01", queue, tableList);
        }

        [TearDown]
        public void TearDown()
        {
            client.Dispose();
        }

        static byte[] Entry(params Guid[] tableIds)
        {
            return Encoding.UTF8.GetBytes(MtdJson.Serialize(new MtdTableListMessage { DisplayId = Guid.NewGuid(), AvailableTableIds = new List<Guid>(tableIds) }));
        }

        void Watch(ulong revision, params Guid[] tableIds)
        {
            client.OnTableListValue(Entry(tableIds), revision, isDirectRead: false);
        }

        void DirectRead(ulong revision, params Guid[] tableIds)
        {
            client.OnTableListValue(Entry(tableIds), revision, isDirectRead: true);
        }

        void RunMainThread()
        {
            queue.Drain((topic, payload) => { });
        }

        bool Shows(Guid tableId)
        {
            MtdTableSummaryMessage message = TestSamples.Roulette(1, "17");
            message.TableId = tableId;
            return tableList.Includes(SnapshotParser.Parse(message));
        }

        // The reported fault: the watch delivered revision 2, then a direct read that had started earlier delivered
        // revision 1, and revision 1 was left on screen.
        [Test]
        public void OlderRevisionArrivingAfterANewerOne_DoesNotReplaceIt()
        {
            Watch(2, tableA, tableB, tableC);
            DirectRead(1, tableA);
            RunMainThread();

            Assert.AreEqual(3, tableList.Count);
            Assert.IsTrue(Shows(tableC));
        }

        [Test]
        public void ReadingTheCurrentRevisionAgain_ChangesNothing()
        {
            Watch(2, tableA, tableB, tableC);
            DirectRead(1, tableA);
            RunMainThread();
            int version = tableList.Version;

            DirectRead(2, tableA, tableB, tableC);
            RunMainThread();

            Assert.AreEqual(3, tableList.Count);
            Assert.AreEqual(version, tableList.Version, "Nothing changed, so nothing should be re-evaluated.");
        }

        [Test]
        public void RevisionsArrivingInOrder_AreEachApplied()
        {
            DirectRead(1, tableA);
            RunMainThread();
            Assert.AreEqual(1, tableList.Count);

            Watch(2, tableA, tableB);
            RunMainThread();
            Assert.AreEqual(2, tableList.Count);

            Watch(3, tableC);
            RunMainThread();
            Assert.AreEqual(1, tableList.Count);
            Assert.IsTrue(Shows(tableC));
            Assert.IsFalse(Shows(tableA));
        }

        [Test]
        public void SameRevisionFromBothPaths_IsAppliedOnce()
        {
            Watch(1, tableA);
            DirectRead(1, tableA);
            RunMainThread();

            Assert.AreEqual(1, tableList.Version);
        }

        [Test]
        public void DeletedEntry_EmptiesTheList()
        {
            Watch(1, tableA, tableB);
            RunMainThread();

            client.OnTableListValue(null, 2, isDirectRead: false);
            RunMainThread();

            Assert.IsTrue(tableList.HasList);
            Assert.AreEqual(0, tableList.Count);
        }

        [Test]
        public void EntryThatCannotBeRead_LeavesTheListInUse_AndANewerOneStillApplies()
        {
            Watch(1, tableA, tableB);
            RunMainThread();

            client.OnTableListValue(Encoding.UTF8.GetBytes("{ not json"), 2, isDirectRead: false);
            RunMainThread();
            Assert.AreEqual(2, tableList.Count);

            // Revision 2 was never put in force, so its repair under the same revision is not mistaken for old news.
            DirectRead(2, tableC);
            RunMainThread();
            Assert.AreEqual(1, tableList.Count);
            Assert.IsTrue(Shows(tableC));
        }

        // A bucket deleted and made again on the server starts its revisions over. One lower revision is only a late
        // arrival; the same lower revision from two direct reads in a row is what the server holds now.
        [Test]
        public void LowerRevisionFromTwoDirectReadsRunning_IsTakenAsTheServersCurrentList()
        {
            Watch(7, tableA, tableB);
            RunMainThread();

            DirectRead(1, tableC);
            RunMainThread();
            Assert.AreEqual(2, tableList.Count, "One lower revision is not enough.");

            DirectRead(1, tableC);
            RunMainThread();
            Assert.AreEqual(1, tableList.Count);
            Assert.IsTrue(Shows(tableC));

            Watch(2, tableA, tableC);
            RunMainThread();
            Assert.AreEqual(2, tableList.Count, "After the reset, revisions carry on from the new bucket's.");
        }

        [Test]
        public void LowerRevisionSeenOnce_ThenTheCurrentOneAgain_IsForgotten()
        {
            Watch(7, tableA, tableB);
            DirectRead(1, tableC);
            DirectRead(7, tableA, tableB);
            DirectRead(1, tableC);
            RunMainThread();

            Assert.AreEqual(2, tableList.Count, "The two lower reads were not consecutive.");
        }

        [Test]
        public void LowerRevisionFromTheWatch_IsNeverTaken()
        {
            Watch(7, tableA, tableB);
            Watch(1, tableC);
            Watch(1, tableC);
            RunMainThread();

            Assert.AreEqual(2, tableList.Count);
        }
    }
}
