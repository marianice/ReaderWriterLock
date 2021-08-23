using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
/*
 ReaderWriterLock -
 для защиты общих ресурсов, то есть одновременно считываются
 и записываются только в рамках нескольких потоков.
 ReaderWriterLock объявлен на  уровне класса, чтобы был видимым для всех потоков.
 Блокировка чтения-записи — механизм синхронизации, разрешающий одновременное общее чтение
 некоторых разделяемых данных либо их эксклюзивное изменение,
 разграничивая таким образом блокировки на чтение и на запись между собой.
 */

namespace ConsoleApp2
{
    public class Program
    {
        static ReaderWriterLock rwl = new ReaderWriterLock();
        //Определяет общий ресурс, защищенный блокировкой чтения - ReaderWriterLock.
        static int resource = 0;

        const int numThreads = 26;
        static bool running = true;
        static Random rnd = new Random();

        // Статичные
        static int readerTimeouts = 0;
        static int writerTimeouts = 0;
        static int reads = 0;
        static int writes = 0;

        public static void Main(string[] args)
        {
           // Запускает серию потоков для случайного чтения и записи в общий ресурс
            Thread[] t = new Thread[numThreads];
            for (int i = 0; i < numThreads; i++)
            {
                t[i] = new Thread(new ThreadStart(ThreadProc));
                t[i].Name = new String(Convert.ToChar(i + 65), 1);
                t[i].Start();
                if (i > 10)
                    Thread.Sleep(300);
            }

            // Закрывает потоки и ждет пока все они не закончат
            running = false;
            for (int i = 0; i < numThreads; i++)
                t[i].Join();

            Console.WriteLine("\n{0} чтений, {1} записей, {2} тайм-аутов чтения, {3} тайм-аутов записи.",
                  reads, writes, readerTimeouts, writerTimeouts);
            Console.Write("Press ENTER to exit... ");
            Console.ReadLine();
        }
        static void ThreadProc()
        {
            // Рандом для чтения и записи из общего ресурса
            while (running)
            {
                double action = rnd.NextDouble();
                if (action < .8)
                    ReadFromResource(10);
                else if (action < .81)
                    ReleaseRestore(50);
                else if (action < .90)
                    UpgradeDowngrade(100);
                else
                    WriteToResource(100);
            }
        }

        // Для запросов, блокировки и тайм-аутов
        static void ReadFromResource(int timeOut)
        {
            try
            {
                rwl.AcquireReaderLock(timeOut);
                try
                {
                    // безопасное чтение с общего ресурса
                    Display("читает значение ресурса " + resource);
                    Interlocked.Increment(ref reads);
                }
                finally
                {
                    // убедиться что блокировка снята
                    rwl.ReleaseReaderLock();
                }
            }
            catch (ApplicationException)
            {
                // Тайм-аут на блокировку
                Interlocked.Increment(ref readerTimeouts);
            }
        }

        // Обработка запросов, блокировок и тайм-аутов
        static void WriteToResource(int timeOut)
        {
            try
            {
                rwl.AcquireWriterLock(timeOut);
                try
                {
                    // безопасное чтение с общего ресурса
                    resource = rnd.Next(500);
                    Display("записывает значение ресурса " + resource);
                    Interlocked.Increment(ref writes);
                }
                finally
                {
                    // убедиться что блокировка снята
                    rwl.ReleaseWriterLock();
                }
            }
            catch (ApplicationException)
            {
                // Тайм-аут на блокировку
                Interlocked.Increment(ref writerTimeouts);
            }
        }

        // Запросы на блокировку чтения, обнова блокировки до блокировки записи и снова блокировка чтения
        static void UpgradeDowngrade(int timeOut)
        {
            try
            {
                rwl.AcquireReaderLock(timeOut);
                try
                {
                    // безопасное чтение с общего ресурса
                    Display("читает значение ресурса " + resource);
                    Interlocked.Increment(ref reads);

                    // Чтобы записать нужно снять блокировку на чтене и запросить блокировку записи или обновить блокировку чтения.
                    // Обновление блокировки чтения помещает поток в очередь записи позади других потоков, которые могут подождать блокировку записи
                    try
                    {
                        LockCookie lc = rwl.UpgradeToWriterLock(timeOut);
                        try
                        {
                            // безопасное чтение и запись в общий ресурс
                            resource = rnd.Next(500);
                            Display("записывает значение ресурса " + resource);
                            Interlocked.Increment(ref writes);
                        }
                        finally
                        {
                            // проверка блокировки
                            rwl.DowngradeFromWriterLock(ref lc);
                        }
                    }
                    catch (ApplicationException)
                    {
                        // обнова тайм-аута
                        Interlocked.Increment(ref writerTimeouts);
                    }

                    // безопасное чтение при блокировке
                    Display("читает значение ресурса " + resource);
                    Interlocked.Increment(ref reads);
                }
                finally
                {
                    // проверка блокировки
                    rwl.ReleaseReaderLock();
                }
            }
            catch (ApplicationException)
            {
                // тайм-аут чтения
                Interlocked.Increment(ref readerTimeouts);
            }
        }


        static void ReleaseRestore(int timeOut)
        {
            int lastWriter;

            try
            {
                rwl.AcquireReaderLock(timeOut);
                try
                {                
                    // безопасное чтение для этого потока из общего ресурса, читаем и присваиваем
                    int resourceValue = resource;     
                    Display("читает значение ресурса " + resourceValue);
                    Interlocked.Increment(ref reads);
                    lastWriter = rwl.WriterSeqNum;
                    LockCookie lc = rwl.ReleaseLock();

                    Thread.Sleep(rnd.Next(250));
                    rwl.RestoreLock(ref lc);


                    if (rwl.AnyWritersSince(lastWriter))
                    {
                        resourceValue = resource;
                        Interlocked.Increment(ref reads);
                        Display("ресурс изменился " + resourceValue);
                    }
                    else
                    {
                        Display("ресурс не изменился " + resourceValue);
                    }
                }
                finally
                {
                    // проверка блокировки
                    rwl.ReleaseReaderLock();
                }
            }
            catch (ApplicationException)
            {
                // тайм-аут чтения
                Interlocked.Increment(ref readerTimeouts);
            }
        }

        // вывод на экран
        static void Display(string msg)
        {
            Console.Write("Поток {0} {1}.       \r", Thread.CurrentThread.Name, msg);
        }
    }
}
