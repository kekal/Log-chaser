using System;
using System.Collections.Generic;
using System.Threading;

namespace log_chaser
{
    public class TaskManager
    {
        public List<ActElement> _actList = new List<ActElement>();

        private object _locker = new object();

        public TaskManager()
        {
            var taskManagerThread = new Thread(ActCycle) { Name = "loopThread", IsBackground = true };
            taskManagerThread.Start();
        }


        /// <summary> добавление новых методов в список задач </summary>
        public void AddFirst(Action threadAct, Action mainDispAct = null, byte id = 0)
        {
            _actList.Insert(0,new ActElement(threadAct, mainDispAct, id));
        }

        /// <summary> добавление новых методов в список задач </summary>
        public void Add(Action threadAct, Action mainDispAct = null, byte id = 0)
        {
            _actList.Add(new ActElement(threadAct, mainDispAct, id));
        }

        /// <summary> удаление заданий с указанным id  </summary>
        public void Remove( byte id)
        {
            for (var i = 0; i < _actList.Count; i++)
            {
                var actElement = _actList[i];

                if (actElement.Id != id) continue;

                _actList.Remove(actElement); 
                i--;
            }
        }
        /// <summary> Очистка списка заданий </summary>
        public void FlushAll()
        {
            _actList.Clear();
        }

        private void ActCycle()
        {
            while (true)
            {
                if (_actList.Count > 0)
                {
                    lock (_locker)
                    {

                        var currentActElement = _actList[0];
                        _actList.RemoveAt(0);

                        if (currentActElement != null && currentActElement.ThreadAct != null) currentActElement.ThreadAct();

                        if (currentActElement != null && currentActElement.InMainAct != null) LogChaser.Wm.InMainDispatch(currentActElement.InMainAct);

                        continue;
                    }
                }
                Thread.Sleep(5);
            }
        }
    }

    /// <summary> класс элемента списка заданий </summary>
    public class ActElement
    {
        public readonly Action ThreadAct;
        public readonly Action InMainAct;
        public readonly byte   Id;

        public ActElement( Action threadAct, Action inMainAct, byte id = 0 )
        {
            ThreadAct = threadAct;
            InMainAct = inMainAct;
            Id = id;
        }
    }
}
