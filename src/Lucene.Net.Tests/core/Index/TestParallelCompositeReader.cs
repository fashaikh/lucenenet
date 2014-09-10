using System;
using Lucene.Net.Documents;

namespace Lucene.Net.Index
{
    using Lucene.Net.Randomized.Generators;
    using Lucene.Net.Search;
    using NUnit.Framework;
    using AlreadyClosedException = Lucene.Net.Store.AlreadyClosedException;
    using Directory = Lucene.Net.Store.Directory;
    using Document = Documents.Document;
    using Field = Field;
    using LuceneTestCase = Lucene.Net.Util.LuceneTestCase;

    /*
         * Licensed to the Apache Software Foundation (ASF) under one or more
         * contributor license agreements.  See the NOTICE file distributed with
         * this work for additional information regarding copyright ownership.
         * The ASF licenses this file to You under the Apache License, Version 2.0
         * (the "License"); you may not use this file except in compliance with
         * the License.  You may obtain a copy of the License at
         *
         *     http://www.apache.org/licenses/LICENSE-2.0
         *
         * Unless required by applicable law or agreed to in writing, software
         * distributed under the License is distributed on an "AS IS" BASIS,
         * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
         * See the License for the specific language governing permissions and
         * limitations under the License.
         */

    using MockAnalyzer = Lucene.Net.Analysis.MockAnalyzer;
    using Occur = Lucene.Net.Search.BooleanClause.Occur;
    using ReaderClosedListener = Lucene.Net.Index.IndexReader.ReaderClosedListener;

    [TestFixture]
    public class TestParallelCompositeReader : LuceneTestCase
    {
        private IndexSearcher Parallel_Renamed, Single_Renamed;
        private Directory Dir, Dir1, Dir2;

        [Test]
        public virtual void TestQueries()
        {
            Single_Renamed = Single(Random(), false);
            Parallel_Renamed = Parallel(Random(), false);

            Queries();

            Single_Renamed.IndexReader.Dispose();
            Single_Renamed = null;
            Parallel_Renamed.IndexReader.Dispose();
            Parallel_Renamed = null;
            Dir.Dispose();
            Dir = null;
            Dir1.Dispose();
            Dir1 = null;
            Dir2.Dispose();
            Dir2 = null;
        }

        [Test]
        public virtual void TestQueriesCompositeComposite()
        {
            Single_Renamed = Single(Random(), true);
            Parallel_Renamed = Parallel(Random(), true);

            Queries();

            Single_Renamed.IndexReader.Dispose();
            Single_Renamed = null;
            Parallel_Renamed.IndexReader.Dispose();
            Parallel_Renamed = null;
            Dir.Dispose();
            Dir = null;
            Dir1.Dispose();
            Dir1 = null;
            Dir2.Dispose();
            Dir2 = null;
        }

        private void Queries()
        {
            QueryTest(new TermQuery(new Term("f1", "v1")));
            QueryTest(new TermQuery(new Term("f1", "v2")));
            QueryTest(new TermQuery(new Term("f2", "v1")));
            QueryTest(new TermQuery(new Term("f2", "v2")));
            QueryTest(new TermQuery(new Term("f3", "v1")));
            QueryTest(new TermQuery(new Term("f3", "v2")));
            QueryTest(new TermQuery(new Term("f4", "v1")));
            QueryTest(new TermQuery(new Term("f4", "v2")));

            BooleanQuery bq1 = new BooleanQuery();
            bq1.Add(new TermQuery(new Term("f1", "v1")), Occur.MUST);
            bq1.Add(new TermQuery(new Term("f4", "v1")), Occur.MUST);
            QueryTest(bq1);
        }

        [Test]
        public virtual void TestRefCounts1()
        {
            Directory dir1 = GetDir1(Random());
            Directory dir2 = GetDir2(Random());
            DirectoryReader ir1, ir2;
            // close subreaders, ParallelReader will not change refCounts, but close on its own close
            ParallelCompositeReader pr = new ParallelCompositeReader(ir1 = DirectoryReader.Open(dir1), ir2 = DirectoryReader.Open(dir2));
            IndexReader psub1 = pr.GetSequentialSubReaders()[0];
            // check RefCounts
            Assert.AreEqual(1, ir1.RefCount);
            Assert.AreEqual(1, ir2.RefCount);
            Assert.AreEqual(1, psub1.RefCount);
            pr.Dispose();
            Assert.AreEqual(0, ir1.RefCount);
            Assert.AreEqual(0, ir2.RefCount);
            Assert.AreEqual(0, psub1.RefCount);
            dir1.Dispose();
            dir2.Dispose();
        }

        [Test]
        public virtual void TestRefCounts2()
        {
            Directory dir1 = GetDir1(Random());
            Directory dir2 = GetDir2(Random());
            DirectoryReader ir1 = DirectoryReader.Open(dir1);
            DirectoryReader ir2 = DirectoryReader.Open(dir2);

            // don't close subreaders, so ParallelReader will increment refcounts
            ParallelCompositeReader pr = new ParallelCompositeReader(false, ir1, ir2);
            IndexReader psub1 = pr.GetSequentialSubReaders()[0];
            // check RefCounts
            Assert.AreEqual(2, ir1.RefCount);
            Assert.AreEqual(2, ir2.RefCount);
            Assert.AreEqual(1, psub1.RefCount, "refCount must be 1, as the synthetic reader was created by ParallelCompositeReader");
            pr.Dispose();
            Assert.AreEqual(1, ir1.RefCount);
            Assert.AreEqual(1, ir2.RefCount);
            Assert.AreEqual(0, psub1.RefCount, "refcount must be 0 because parent was closed");
            ir1.Dispose();
            ir2.Dispose();
            Assert.AreEqual(0, ir1.RefCount);
            Assert.AreEqual(0, ir2.RefCount);
            Assert.AreEqual(0, psub1.RefCount, "refcount should not change anymore");
            dir1.Dispose();
            dir2.Dispose();
        }

        // closeSubreaders=false
        [Test]
        public virtual void TestReaderClosedListener1()
        {
            Directory dir1 = GetDir1(Random());
            CompositeReader ir1 = DirectoryReader.Open(dir1);

            // with overlapping
            ParallelCompositeReader pr = new ParallelCompositeReader(false, new CompositeReader[] { ir1 }, new CompositeReader[] { ir1 });

            int[] listenerClosedCount = new int[1];

            Assert.AreEqual(3, pr.Leaves().Count);

            foreach (AtomicReaderContext cxt in pr.Leaves())
            {
                cxt.Reader().AddReaderClosedListener(new ReaderClosedListenerAnonymousInnerClassHelper(this, listenerClosedCount));
            }
            pr.Dispose();
            ir1.Dispose();
            Assert.AreEqual(3, listenerClosedCount[0]);
            dir1.Dispose();
        }

        private class ReaderClosedListenerAnonymousInnerClassHelper : ReaderClosedListener
        {
            private readonly TestParallelCompositeReader OuterInstance;

            private int[] ListenerClosedCount;

            public ReaderClosedListenerAnonymousInnerClassHelper(TestParallelCompositeReader outerInstance, int[] listenerClosedCount)
            {
                this.OuterInstance = outerInstance;
                this.ListenerClosedCount = listenerClosedCount;
            }

            public void OnClose(IndexReader reader)
            {
                ListenerClosedCount[0]++;
            }
        }

        // closeSubreaders=true
        [Test]
        public virtual void TestReaderClosedListener2()
        {
            Directory dir1 = GetDir1(Random());
            CompositeReader ir1 = DirectoryReader.Open(dir1);

            // with overlapping
            ParallelCompositeReader pr = new ParallelCompositeReader(true, new CompositeReader[] { ir1 }, new CompositeReader[] { ir1 });

            int[] listenerClosedCount = new int[1];

            Assert.AreEqual(3, pr.Leaves().Count);

            foreach (AtomicReaderContext cxt in pr.Leaves())
            {
                cxt.Reader().AddReaderClosedListener(new ReaderClosedListenerAnonymousInnerClassHelper2(this, listenerClosedCount));
            }
            pr.Dispose();
            Assert.AreEqual(3, listenerClosedCount[0]);
            dir1.Dispose();
        }

        private class ReaderClosedListenerAnonymousInnerClassHelper2 : ReaderClosedListener
        {
            private readonly TestParallelCompositeReader OuterInstance;

            private int[] ListenerClosedCount;

            public ReaderClosedListenerAnonymousInnerClassHelper2(TestParallelCompositeReader outerInstance, int[] listenerClosedCount)
            {
                this.OuterInstance = outerInstance;
                this.ListenerClosedCount = listenerClosedCount;
            }

            public void OnClose(IndexReader reader)
            {
                ListenerClosedCount[0]++;
            }
        }

        [Test]
        public virtual void TestCloseInnerReader()
        {
            Directory dir1 = GetDir1(Random());
            CompositeReader ir1 = DirectoryReader.Open(dir1);
            Assert.AreEqual(1, ir1.GetSequentialSubReaders()[0].RefCount);

            // with overlapping
            ParallelCompositeReader pr = new ParallelCompositeReader(true, new CompositeReader[] { ir1 }, new CompositeReader[] { ir1 });

            IndexReader psub = pr.GetSequentialSubReaders()[0];
            Assert.AreEqual(1, psub.RefCount);

            ir1.Dispose();

            Assert.AreEqual(1, psub.RefCount, "refCount of synthetic subreader should be unchanged");
            try
            {
                psub.Document(0);
                Assert.Fail("Subreader should be already closed because inner reader was closed!");
            }
            catch (AlreadyClosedException e)
            {
                // pass
            }

            try
            {
                pr.Document(0);
                Assert.Fail("ParallelCompositeReader should be already closed because inner reader was closed!");
            }
            catch (AlreadyClosedException e)
            {
                // pass
            }

            // noop:
            pr.Dispose();
            Assert.AreEqual(0, psub.RefCount);
            dir1.Dispose();
        }

        [Test]
        public virtual void TestIncompatibleIndexes1()
        {
            // two documents:
            Directory dir1 = GetDir1(Random());

            // one document only:
            Directory dir2 = NewDirectory();
            IndexWriter w2 = new IndexWriter(dir2, NewIndexWriterConfig(TEST_VERSION_CURRENT, new MockAnalyzer(Random())));
            Document d3 = new Document();

            d3.Add(NewTextField("f3", "v1", Field.Store.YES));
            w2.AddDocument(d3);
            w2.Dispose();

            DirectoryReader ir1 = DirectoryReader.Open(dir1), ir2 = DirectoryReader.Open(dir2);
            try
            {
                new ParallelCompositeReader(ir1, ir2);
                Assert.Fail("didn't get expected exception: indexes don't have same number of documents");
            }
            catch (System.ArgumentException e)
            {
                // expected exception
            }
            try
            {
                new ParallelCompositeReader(Random().NextBoolean(), ir1, ir2);
                Assert.Fail("didn't get expected exception: indexes don't have same number of documents");
            }
            catch (System.ArgumentException e)
            {
                // expected exception
            }
            Assert.AreEqual(1, ir1.RefCount);
            Assert.AreEqual(1, ir2.RefCount);
            ir1.Dispose();
            ir2.Dispose();
            Assert.AreEqual(0, ir1.RefCount);
            Assert.AreEqual(0, ir2.RefCount);
            dir1.Dispose();
            dir2.Dispose();
        }

        [Test]
        public virtual void TestIncompatibleIndexes2()
        {
            Directory dir1 = GetDir1(Random());
            Directory dir2 = GetInvalidStructuredDir2(Random());

            DirectoryReader ir1 = DirectoryReader.Open(dir1), ir2 = DirectoryReader.Open(dir2);
            CompositeReader[] readers = new CompositeReader[] { ir1, ir2 };
            try
            {
                new ParallelCompositeReader(readers);
                Assert.Fail("didn't get expected exception: indexes don't have same subreader structure");
            }
            catch (System.ArgumentException e)
            {
                // expected exception
            }
            try
            {
                new ParallelCompositeReader(Random().NextBoolean(), readers, readers);
                Assert.Fail("didn't get expected exception: indexes don't have same subreader structure");
            }
            catch (System.ArgumentException e)
            {
                // expected exception
            }
            Assert.AreEqual(1, ir1.RefCount);
            Assert.AreEqual(1, ir2.RefCount);
            ir1.Dispose();
            ir2.Dispose();
            Assert.AreEqual(0, ir1.RefCount);
            Assert.AreEqual(0, ir2.RefCount);
            dir1.Dispose();
            dir2.Dispose();
        }

        [Test]
        public virtual void TestIncompatibleIndexes3()
        {
            Directory dir1 = GetDir1(Random());
            Directory dir2 = GetDir2(Random());

            CompositeReader ir1 = new MultiReader(DirectoryReader.Open(dir1), SlowCompositeReaderWrapper.Wrap(DirectoryReader.Open(dir1))), ir2 = new MultiReader(DirectoryReader.Open(dir2), DirectoryReader.Open(dir2));
            CompositeReader[] readers = new CompositeReader[] { ir1, ir2 };
            try
            {
                new ParallelCompositeReader(readers);
                Assert.Fail("didn't get expected exception: indexes don't have same subreader structure");
            }
            catch (System.ArgumentException e)
            {
                // expected exception
            }
            try
            {
                new ParallelCompositeReader(Random().NextBoolean(), readers, readers);
                Assert.Fail("didn't get expected exception: indexes don't have same subreader structure");
            }
            catch (System.ArgumentException e)
            {
                // expected exception
            }
            Assert.AreEqual(1, ir1.RefCount);
            Assert.AreEqual(1, ir2.RefCount);
            ir1.Dispose();
            ir2.Dispose();
            Assert.AreEqual(0, ir1.RefCount);
            Assert.AreEqual(0, ir2.RefCount);
            dir1.Dispose();
            dir2.Dispose();
        }

        [Test]
        public virtual void TestIgnoreStoredFields()
        {
            Directory dir1 = GetDir1(Random());
            Directory dir2 = GetDir2(Random());
            CompositeReader ir1 = DirectoryReader.Open(dir1);
            CompositeReader ir2 = DirectoryReader.Open(dir2);

            // with overlapping
            ParallelCompositeReader pr = new ParallelCompositeReader(false, new CompositeReader[] { ir1, ir2 }, new CompositeReader[] { ir1 });
            Assert.AreEqual("v1", pr.Document(0).Get("f1"));
            Assert.AreEqual("v1", pr.Document(0).Get("f2"));
            Assert.IsNull(pr.Document(0).Get("f3"));
            Assert.IsNull(pr.Document(0).Get("f4"));
            // check that fields are there
            AtomicReader slow = SlowCompositeReaderWrapper.Wrap(pr);
            Assert.IsNotNull(slow.Terms("f1"));
            Assert.IsNotNull(slow.Terms("f2"));
            Assert.IsNotNull(slow.Terms("f3"));
            Assert.IsNotNull(slow.Terms("f4"));
            pr.Dispose();

            // no stored fields at all
            pr = new ParallelCompositeReader(false, new CompositeReader[] { ir2 }, new CompositeReader[0]);
            Assert.IsNull(pr.Document(0).Get("f1"));
            Assert.IsNull(pr.Document(0).Get("f2"));
            Assert.IsNull(pr.Document(0).Get("f3"));
            Assert.IsNull(pr.Document(0).Get("f4"));
            // check that fields are there
            slow = SlowCompositeReaderWrapper.Wrap(pr);
            Assert.IsNull(slow.Terms("f1"));
            Assert.IsNull(slow.Terms("f2"));
            Assert.IsNotNull(slow.Terms("f3"));
            Assert.IsNotNull(slow.Terms("f4"));
            pr.Dispose();

            // without overlapping
            pr = new ParallelCompositeReader(true, new CompositeReader[] { ir2 }, new CompositeReader[] { ir1 });
            Assert.AreEqual("v1", pr.Document(0).Get("f1"));
            Assert.AreEqual("v1", pr.Document(0).Get("f2"));
            Assert.IsNull(pr.Document(0).Get("f3"));
            Assert.IsNull(pr.Document(0).Get("f4"));
            // check that fields are there
            slow = SlowCompositeReaderWrapper.Wrap(pr);
            Assert.IsNull(slow.Terms("f1"));
            Assert.IsNull(slow.Terms("f2"));
            Assert.IsNotNull(slow.Terms("f3"));
            Assert.IsNotNull(slow.Terms("f4"));
            pr.Dispose();

            // no main readers
            try
            {
                new ParallelCompositeReader(true, new CompositeReader[0], new CompositeReader[] { ir1 });
                Assert.Fail("didn't get expected exception: need a non-empty main-reader array");
            }
            catch (System.ArgumentException iae)
            {
                // pass
            }

            dir1.Dispose();
            dir2.Dispose();
        }

        [Test]
        public virtual void TestToString()
        {
            Directory dir1 = GetDir1(Random());
            CompositeReader ir1 = DirectoryReader.Open(dir1);
            ParallelCompositeReader pr = new ParallelCompositeReader(new CompositeReader[] { ir1 });

            string s = pr.ToString();
            Assert.IsTrue(s.StartsWith("ParallelCompositeReader(ParallelAtomicReader("), "toString incorrect: " + s);

            pr.Dispose();
            dir1.Dispose();
        }

        [Test]
        public virtual void TestToStringCompositeComposite()
        {
            Directory dir1 = GetDir1(Random());
            CompositeReader ir1 = DirectoryReader.Open(dir1);
            ParallelCompositeReader pr = new ParallelCompositeReader(new CompositeReader[] { new MultiReader(ir1) });

            string s = pr.ToString();
            Assert.IsTrue(s.StartsWith("ParallelCompositeReader(ParallelCompositeReader(ParallelAtomicReader("), "toString incorrect: " + s);

            pr.Dispose();
            dir1.Dispose();
        }

        private void QueryTest(Query query)
        {
            ScoreDoc[] parallelHits = Parallel_Renamed.Search(query, null, 1000).ScoreDocs;
            ScoreDoc[] singleHits = Single_Renamed.Search(query, null, 1000).ScoreDocs;
            Assert.AreEqual(parallelHits.Length, singleHits.Length);
            for (int i = 0; i < parallelHits.Length; i++)
            {
                Assert.AreEqual(parallelHits[i].Score, singleHits[i].Score, 0.001f);
                Document docParallel = Parallel_Renamed.Doc(parallelHits[i].Doc);
                Document docSingle = Single_Renamed.Doc(singleHits[i].Doc);
                Assert.AreEqual(docParallel.Get("f1"), docSingle.Get("f1"));
                Assert.AreEqual(docParallel.Get("f2"), docSingle.Get("f2"));
                Assert.AreEqual(docParallel.Get("f3"), docSingle.Get("f3"));
                Assert.AreEqual(docParallel.Get("f4"), docSingle.Get("f4"));
            }
        }

        // Fields 1-4 indexed together:
        private IndexSearcher Single(Random random, bool compositeComposite)
        {
            Dir = NewDirectory();
            IndexWriter w = new IndexWriter(Dir, NewIndexWriterConfig(TEST_VERSION_CURRENT, new MockAnalyzer(random)));
            Document d1 = new Document();
            d1.Add(NewTextField("f1", "v1", Field.Store.YES));
            d1.Add(NewTextField("f2", "v1", Field.Store.YES));
            d1.Add(NewTextField("f3", "v1", Field.Store.YES));
            d1.Add(NewTextField("f4", "v1", Field.Store.YES));
            w.AddDocument(d1);
            Document d2 = new Document();
            d2.Add(NewTextField("f1", "v2", Field.Store.YES));
            d2.Add(NewTextField("f2", "v2", Field.Store.YES));
            d2.Add(NewTextField("f3", "v2", Field.Store.YES));
            d2.Add(NewTextField("f4", "v2", Field.Store.YES));
            w.AddDocument(d2);
            Document d3 = new Document();
            d3.Add(NewTextField("f1", "v3", Field.Store.YES));
            d3.Add(NewTextField("f2", "v3", Field.Store.YES));
            d3.Add(NewTextField("f3", "v3", Field.Store.YES));
            d3.Add(NewTextField("f4", "v3", Field.Store.YES));
            w.AddDocument(d3);
            Document d4 = new Document();
            d4.Add(NewTextField("f1", "v4", Field.Store.YES));
            d4.Add(NewTextField("f2", "v4", Field.Store.YES));
            d4.Add(NewTextField("f3", "v4", Field.Store.YES));
            d4.Add(NewTextField("f4", "v4", Field.Store.YES));
            w.AddDocument(d4);
            w.Dispose();

            CompositeReader ir;
            if (compositeComposite)
            {
                ir = new MultiReader(DirectoryReader.Open(Dir), DirectoryReader.Open(Dir));
            }
            else
            {
                ir = DirectoryReader.Open(Dir);
            }
            return NewSearcher(ir);
        }

        // Fields 1 & 2 in one index, 3 & 4 in other, with ParallelReader:
        private IndexSearcher Parallel(Random random, bool compositeComposite)
        {
            Dir1 = GetDir1(random);
            Dir2 = GetDir2(random);
            CompositeReader rd1, rd2;
            if (compositeComposite)
            {
                rd1 = new MultiReader(DirectoryReader.Open(Dir1), DirectoryReader.Open(Dir1));
                rd2 = new MultiReader(DirectoryReader.Open(Dir2), DirectoryReader.Open(Dir2));
                Assert.AreEqual(2, rd1.Context.Children().Count);
                Assert.AreEqual(2, rd2.Context.Children().Count);
            }
            else
            {
                rd1 = DirectoryReader.Open(Dir1);
                rd2 = DirectoryReader.Open(Dir2);
                Assert.AreEqual(3, rd1.Context.Children().Count);
                Assert.AreEqual(3, rd2.Context.Children().Count);
            }
            ParallelCompositeReader pr = new ParallelCompositeReader(rd1, rd2);
            return NewSearcher(pr);
        }

        // subreader structure: (1,2,1)
        private Directory GetDir1(Random random)
        {
            Directory dir1 = NewDirectory();
            IndexWriter w1 = new IndexWriter(dir1, NewIndexWriterConfig(TEST_VERSION_CURRENT, new MockAnalyzer(random)).SetMergePolicy(NoMergePolicy.NO_COMPOUND_FILES));
            Document d1 = new Document();
            d1.Add(NewTextField("f1", "v1", Field.Store.YES));
            d1.Add(NewTextField("f2", "v1", Field.Store.YES));
            w1.AddDocument(d1);
            w1.Commit();
            Document d2 = new Document();
            d2.Add(NewTextField("f1", "v2", Field.Store.YES));
            d2.Add(NewTextField("f2", "v2", Field.Store.YES));
            w1.AddDocument(d2);
            Document d3 = new Document();
            d3.Add(NewTextField("f1", "v3", Field.Store.YES));
            d3.Add(NewTextField("f2", "v3", Field.Store.YES));
            w1.AddDocument(d3);
            w1.Commit();
            Document d4 = new Document();
            d4.Add(NewTextField("f1", "v4", Field.Store.YES));
            d4.Add(NewTextField("f2", "v4", Field.Store.YES));
            w1.AddDocument(d4);
            w1.Dispose();
            return dir1;
        }

        // subreader structure: (1,2,1)
        private Directory GetDir2(Random random)
        {
            Directory dir2 = NewDirectory();
            IndexWriter w2 = new IndexWriter(dir2, NewIndexWriterConfig(TEST_VERSION_CURRENT, new MockAnalyzer(random)).SetMergePolicy(NoMergePolicy.NO_COMPOUND_FILES));
            Document d1 = new Document();
            d1.Add(NewTextField("f3", "v1", Field.Store.YES));
            d1.Add(NewTextField("f4", "v1", Field.Store.YES));
            w2.AddDocument(d1);
            w2.Commit();
            Document d2 = new Document();
            d2.Add(NewTextField("f3", "v2", Field.Store.YES));
            d2.Add(NewTextField("f4", "v2", Field.Store.YES));
            w2.AddDocument(d2);
            Document d3 = new Document();
            d3.Add(NewTextField("f3", "v3", Field.Store.YES));
            d3.Add(NewTextField("f4", "v3", Field.Store.YES));
            w2.AddDocument(d3);
            w2.Commit();
            Document d4 = new Document();
            d4.Add(NewTextField("f3", "v4", Field.Store.YES));
            d4.Add(NewTextField("f4", "v4", Field.Store.YES));
            w2.AddDocument(d4);
            w2.Dispose();
            return dir2;
        }

        // this dir has a different subreader structure (1,1,2);
        private Directory GetInvalidStructuredDir2(Random random)
        {
            Directory dir2 = NewDirectory();
            IndexWriter w2 = new IndexWriter(dir2, NewIndexWriterConfig(TEST_VERSION_CURRENT, new MockAnalyzer(random)).SetMergePolicy(NoMergePolicy.NO_COMPOUND_FILES));
            Document d1 = new Document();
            d1.Add(NewTextField("f3", "v1", Field.Store.YES));
            d1.Add(NewTextField("f4", "v1", Field.Store.YES));
            w2.AddDocument(d1);
            w2.Commit();
            Document d2 = new Document();
            d2.Add(NewTextField("f3", "v2", Field.Store.YES));
            d2.Add(NewTextField("f4", "v2", Field.Store.YES));
            w2.AddDocument(d2);
            w2.Commit();
            Document d3 = new Document();
            d3.Add(NewTextField("f3", "v3", Field.Store.YES));
            d3.Add(NewTextField("f4", "v3", Field.Store.YES));
            w2.AddDocument(d3);
            Document d4 = new Document();
            d4.Add(NewTextField("f3", "v4", Field.Store.YES));
            d4.Add(NewTextField("f4", "v4", Field.Store.YES));
            w2.AddDocument(d4);
            w2.Dispose();
            return dir2;
        }
    }
}