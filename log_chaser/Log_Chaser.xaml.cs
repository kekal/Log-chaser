using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DataFormats = System.Windows.DataFormats;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using RichTextBox = System.Windows.Controls.RichTextBox;
using System.Text.RegularExpressions;

namespace log_chaser
{
    /// <summary>
    /// Interaction logic for Log_Chaser.xaml
    /// </summary>
    public partial class LogChaser
    {
        public SortedDictionary<string, SortedDictionary<string, uint>> SynonimBase;
        public List<string> todeleteList;


        /// <summary> экземпляр класса TaskManager </summary>
        private readonly TaskManager _task = new TaskManager();

        // Объект типа АКНО
        public static LogChaser Wm;

        /// <summary> неудаляемая часть лога </summary>
        private StringBuilder _history = new StringBuilder();

        //сдвиг времени для одного из чатов и флаг необходимости.
        private byte _timeIncrement;
        private bool _needToFixTime;

        //получаем массив строк содержащих пути к файлам
        private const string Path = (@"c:\Dropbox\Dropbox\Public\log_chaser\logs");

        //список всех строк
        private List<LineElement> _linesList;

        /// <summary> словарь пользователей </summary>
        private SortedDictionary<string, UserElement> _usersList;

        /// <summary> словарь входов сортированный по хостам  </summary>
        private SortedDictionary<string, SortedDictionary<string, uint>> _joinHostslist;
        /// <summary> словарь входов сортированный по именам  </summary>
        private SortedDictionary<string, SortedDictionary<string, uint>> _joinNickslist;

        /// <summary> словарь всех переимнований </summary>
        private SortedDictionary<string, string> _renamelist;

        public LogChaser()
        {
            //создание статической ссылки на объект MainWindow 
            Wm = this;

            InitializeComponent();
        }

        /// <summary> кнопка обработки </summary>
        private void ButtonScanFiles_Click(object sender, RoutedEventArgs e)
        {

            var startTime = DateTime.Now;

            var rawFileNamesArray = Directory.GetFiles(Path);
            
            //вывод информации о директории
            _history.AppendLine("Информация о директории");
            _history.Append("Путь: "); _history.Append(Path); _history.Append("\n");
            _history.Append("Файлов в директории: ");
            _history.AppendLine(rawFileNamesArray.Length.ToString());
            

            //инициализируем список строк
            _linesList = new List<LineElement>();
            //инициализируем словари вхождений
            _joinHostslist = new SortedDictionary<string, SortedDictionary<string, uint>>();
            _joinNickslist = new SortedDictionary<string, SortedDictionary<string, uint>>();

            //инициализируем список переименований
            _renamelist = new SortedDictionary<string, string>();



            //инициализируем список файлов
            var filelist = new List<FileElement>();

            //перебираем все файлы и заполняем список элементов класса filelist (но без LineElement)
            for (var i = 0; i < rawFileNamesArray.Length; i++)
            {
                var index = i;
                //===========================================================================================
                _task.Add(() =>
                    {
                        //определяем дату из пути файла и содаём список путей к файлам
                        DateTime filetime;
                        FileDateParsing(rawFileNamesArray[index], out filetime);
                        filelist.Add(new FileElement(rawFileNamesArray[index], filetime));
                    });
                //===========================================================================================

                //формируем из тела файла filelist[] список LineElement'ов и дополняем им базу LinesList
                _task.Add(() => FileClearing(filelist[index].FilePath, filelist[index]),


                          //выводим информацию о текущем файле
                          () => UpdateLog("Читается файл: " + filelist[index].Time.ToLongDateString() + "\n" +
                                          (index + 1) + " из " + rawFileNamesArray.Length + "\n" +
                                          "всего строк: " + _linesList.Count + "\n"));
            }

            //выводим резюме перебора всех файлов
            _task.Add(null, () =>
                {
                    _history.Append("Всего строк: "); _history.Append(_linesList.Count); _history.Append("\n");
                    _history.Append("Total Memory: "); _history.Append((GC.GetTotalMemory(true)/1000000.0).ToString("0.000")); _history.Append("мб\n");

                    var currentTime = DateTime.Now;
                    var elapsedTicks = currentTime.Ticks - startTime.Ticks;
                    var elapsedSpan = new TimeSpan(elapsedTicks);
                    _history.AppendLine(elapsedSpan.TotalSeconds.ToString("\n0.00 секунд"));
                    _history.AppendLine("КАНЕЦ\n");

                    UpdateLog("");
                });
        }

        /// <summary> возвращает почищенный список строк из файла, путь к которому задаётся в качестве аргумента </summary>
        private void FileClearing(string filePath, FileElement file)
        {
            //выясняем не тот ли это файл, в котором сдвиг во времени и устанавливаем флаг
            _needToFixTime = (filePath == Path + @"\#eternal-haro.2013-02-25.log");

            var dayloglist = (File.ReadAllText(filePath)).Split('\n').ToList();

            foreach (var line in dayloglist)
            {
                if (RegexClass.FirstRegexForPost.Match(line).Success)
                {
                    //расковыриваем строку сообщения на час, минуту, ник и тело строки
                    byte hour, minute;
                    string nick;
                    ushort length;

                    UsersLineParsing(line, out hour, out minute, out nick, out length);

                    if (string.IsNullOrEmpty(nick)) return;

                    //формируем полное время строки
                    var dt = new DateTime(file.Time.Year, file.Time.Month, file.Time.Day, hour, minute, 0);

                    //добавляем список LineElement файла к общей базе строк
                    _linesList.Add(new LineElement(dt, nick, length));
                }


                    //в случае если распознан вход/выход
                else if (RegexClass.FirstRegexForJoin.Match(line).Success)
                {
                    string nick, ident, prov, host;

                    //выделяем информацию из строки
                    JoinLineParsing(line, out nick, out ident, out prov, out host);

                    if (string.IsNullOrEmpty(nick)) return;
                    
                    JoinUpdate(nick, host);
                }


                    //в случае если распознано переименование
                else if (RegexClass.FirstRegexForRename.Match(line).Success)
                {
                    string nick1, nick2;

                    //выделяем информацию из строки
                    RenameLineParsing(line, out nick1, out nick2);

                    if (string.IsNullOrEmpty(nick1) || string.IsNullOrEmpty(nick2)) return;

                    //заносим ники в базу переименований
                    RenameUpdate(nick1, nick2);
                }
            }
        }

        /// <summary> заполняет словари входов сортированные по хосту и по имени соотвественно </summary>
        public void JoinUpdate(string nick, string host)
        {
            if (_joinHostslist.ContainsKey(host))
            {
                if (_joinHostslist[host].ContainsKey(nick)) _joinHostslist[host][nick]++;

                else _joinHostslist[host].Add(nick, 1);
            }

            else _joinHostslist.Add(host, new SortedDictionary<string, uint> {{nick, 1}});


            if (_joinNickslist.ContainsKey(nick))
            {
                if (_joinNickslist[nick].ContainsKey(host)) _joinNickslist[nick][host]++;

                else _joinNickslist[nick].Add(host,1);
            }

            else _joinNickslist.Add(nick, new  SortedDictionary<string, uint> {{host, 1}});
        }

        /// <summary> заполняет словари переименований  </summary>
        public void RenameUpdate(string nick1, string nick2)
        {
            if (nick1 == nick2) return;

            if (_renamelist.ContainsKey(nick1))
            {
                if (_renamelist.ContainsKey(nick2)) return;
                _renamelist.Add(nick2, nick1);
            }

            else if (_renamelist.ContainsKey(nick2))
            {
                _renamelist.Add(nick1, nick2);
            }

            else
            {
                _renamelist.Add(nick1, nick2);
                _renamelist.Add(nick2, nick1);
            }
        }

        /// <summary> слияние двух словарей </summary>
        public SortedDictionary<string, uint> MergeDictionary(SortedDictionary<string, uint> dic1, SortedDictionary<string, uint> dic2)
        {
            var tempenam = new SortedDictionary<string, uint>();
            var enam = dic1.Union(dic2);
            foreach (var user in enam)
            {
                tempenam.Add(user.Key, user.Value);
            }

            return tempenam;
        }

        /// <summary> очистка ника от мусора </summary>
        private string NickClearing(string nick)
        {

            var reg = RegexClass.RegexForNick1.Match(nick);
            if (reg.Success) return null;
            

            reg = RegexClass.RegexForNick2.Match(nick);
            var groupofMatches = reg.Groups;
            nick = groupofMatches[1].ToString();
            

            reg = RegexClass.RegexForNick3.Match(nick);
            groupofMatches = reg.Groups;

            //if (groupofMatches[1].ToString() == "") MessageBox.Show("wtf");
            
            return groupofMatches[1].ToString().ToLower();
            
        }
        
        /// <summary> разбор строки входы/выхода в чат </summary>
        private void JoinLineParsing(string line,  out string nick, out string ident, out string prov,out string host)
        {
            var reg = RegexClass.SecondRegexForJoin.Match(line);
            var groupofMatches = reg.Groups;

            nick = groupofMatches[1].ToString();
            ident = groupofMatches[3].ToString().ToLower();
            prov = groupofMatches[4].ToString().ToLower();
            host = groupofMatches[2].ToString().ToLower();

            

            //очищаем ник
            nick = NickClearing(nick);
            
        }

        /// <summary> разбор строки переименования </summary>
        private void RenameLineParsing(string line, out string nick1, out string nick2)
        {
            var reg = RegexClass.SecondRegexForRename.Match(line);
            var groupofMatches = reg.Groups;

            nick1 = groupofMatches[1].ToString();
            nick2 = groupofMatches[2].ToString();
            

            //очищаем ники
            nick1 = NickClearing(nick1);
            nick2 = NickClearing(nick2);
        }

        /// <summary> разбор строки на час, минуту, имя и длину тела сообщения </summary>
        private void UsersLineParsing(string line, out byte hour, out byte minute, out string nick, out ushort length)
        {
            var reg = RegexClass.SecondRegexForPost.Match(line);
            var groupofMatches = reg.Groups;
            //вычленяем час, минуту, имя и длину сообщения
            var stringHour = groupofMatches[1].ToString();
            var stringMinute = groupofMatches[2].ToString();
            nick = groupofMatches[3].ToString();
            length = (ushort) groupofMatches[4].ToString().Length;

            
            //очищаем ник
            nick = NickClearing(nick);
            

            //конвертируем час и минуту в число
            byte.TryParse(stringHour, out hour);
            byte.TryParse(stringMinute, out minute);

            //пытаемся учесть крироучие мендора 
            if (_needToFixTime && minute == 28 && hour == 14) _timeIncrement = 6;
            else if (_needToFixTime && minute == 22 && hour == 10) _timeIncrement = 0;

            hour -= _timeIncrement;
        }

    
        /// <summary> вычисляет и возвращает дату файла </summary>
        private void FileDateParsing(string filepath, out DateTime now)
        {
            now = new DateTime();

            const string pattern = @"\d\d\d\d-\d\d-\d\d";

            var regex = new Regex(pattern);

            var match = regex.Match(filepath);

            if (!match.Success)
            {
                _task.Add(null, () => UpdateLog("Строка нераспознана\n" + filepath));
                return;
            }

            var dateformatarray = (match.Value).Split('-');

            int year;
            int month;
            int date;

            int.TryParse(dateformatarray[0], out year);
            int.TryParse(dateformatarray[1], out month);
            int.TryParse(dateformatarray[2], out date);

            now = new DateTime(year, month, date);
        }

        /// <summary> обновляет содержимое окна лога и добавляет временно в конец свой аргумент </summary>
        private void UpdateLog(string text)
        {
            Log.Text = _history + text + "\n";
            Log.ScrollToEnd();
        }

        /// <summary> Выводит список всех найденных пользователей </summary>
        public void DisplayAllUsers()
        {
            // _usersList = _usersList.OrderBy(user => user.Key).ToDictionary(pair => pair.Key, pair => pair.Value);

            _history.Append("Список юзеров\n\n");

            foreach (var user1 in _usersList)
            {
                var user = user1;
                _task.Add(
                    () => _history.Append( user.Value._nickNames + "\n"),
                    () => UpdateLog("")
                    );
            }
        }

        /// <summary> класс элемента базы данных файлов </summary>
        private class FileElement
        {

            public readonly string FilePath;
            public readonly DateTime Time;

            public FileElement(string filePath, DateTime time)
            {

                FilePath = filePath;
                Time = time;

            }
        }

        /// <summary> класс элемента базы данных файлов </summary>
        public class LineElement
        {
            public readonly DateTime Time;
            public readonly string Nick;
            public readonly ushort LineLength;


            public LineElement(DateTime time, string nick, ushort lineLength)
            {
                Time = time;
                Nick = nick;
                LineLength = lineLength;
            }
        }

        /// <summary> класс элемента базы данных файлов </summary>
        public class UserElement
        {
            public List<string> _nickNames;
            public List<DateTime> _moments;
            public List<ushort> StringFatness;


            public UserElement(List<string> nickNames, List<DateTime> moments, List<ushort> stringFatness)
            {
                _nickNames = nickNames;
                _moments = moments;
                StringFatness = stringFatness;
            }

        }

        /// <summary> класс экземпляров регулярных выражений </summary>
        private static class RegexClass
        {
            public static readonly Regex RegexForNick3 = new Regex(@"^(.*?)\d*$");

            public static readonly Regex RegexForNick2 = new Regex(@"^[\[\]_|\^]*([^\[\]|\^]+)[\[\]_|\^]*?.*\d*$");

            public static readonly Regex RegexForNick1 = new Regex(@"^((?:Guest[\d]+)|(?:(?i)andchat(?-i).*)|(?:mode\/)|(?:ServerMode\/\#eternal\-haro)|(?:newbie[\d]*))$");

            


            public static readonly Regex FirstRegexForPost = new Regex(@"^\d\d:\d\d <.*");

            public static readonly Regex SecondRegexForPost = new Regex(@"^(\d\d):(\d\d)\s+[<\*][+%&~@ ]([^\b]+?)>\s?(.*)");




            public static readonly Regex FirstRegexForJoin = new Regex(@"^\d\d:\d\d -!- (?!(?:Guest[\d]+)|(?:mode\/)|(?:ServerMode\/\#eternal\-haro)|(?:newbie[\d]*))([^\s]+)\s\[.*");
            
            public static readonly Regex SecondRegexForJoin = new Regex(@"^\d\d:\d\d -!- ([^\s]+)\s\[\~?((.*)@(.+?))\].*");




            public static readonly Regex FirstRegexForRename = new Regex(@"^\d\d:\d\d -!-.+ is now known as .+");

            public static readonly Regex SecondRegexForRename = new Regex(@"^\d\d:\d\d \-\!\- ([^\s]+) is now known as ([^\s]+)");
            
            
            //public static readonly Regex RegexForPost =
            //    //new Regex(@"^(\d\d):(\d\d)\s+[<\*][+%&~@ ].*?(?!Guest\d+)[_|\d]?(?i)(.+?)(?-i)[_|\d]?>\s?(.*)");
            //    new Regex(@"^(\d\d):(\d\d)\s+[<\*][+%&~@ ].*?(?!Guest\d+)[_|\d]?(.+?)[_|\d]?(?!(?:(?:work)|(?:away)))[_|\d]?>\s?(.*)", RegexOptions.IgnoreCase);

            //public static readonly Regex FirstRegexForJoin =
            //    //new Regex(@"^\d\d:\d\d -!- (?!(?:(?:Guest\d+?)|(?:mode\/)|(?:Irssi:)|(?:newbie\d*)))([^_\|\^\[\]\s]+)[\d]+?.*(?!(?:(?:work)|(?:away)|(?:afk)))[_|\d]?\s\[\~?((.*)@(.+?))\].*");
            //    new Regex(@"^\d\d:\d\d -!- ([^\s]+)\s\[\~?((.*)@(.+?))\].*", RegexOptions.IgnoreCase);

            //public static readonly Regex RegexForRename =
            //    new Regex(@"^\d\d:\d\d \-\!\- (?i)(?!(?:Guest\d+?))[_|]?(.+?)[_|]? is now known as (?!Guest\d+?)[_|]?(.+?)[_|]?(?-i)", RegexOptions.IgnoreCase);
        }

        /// <summary> обработчик делегата который требуется выполнять в основном потоке </summary>
        public void InMainDispatch(Action dlg)
        {
            if (Thread.CurrentThread.Name == "MainThread") dlg();

            else
            {
                Wm.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action<string>(delegate { dlg(); }), "?");
            }
        }

        /// <summary> обновление/добавление в базу _usersList информации о пользователе извлечённой из LineElement </summary>
        public void UpdateStatistic(LineElement line)
        {
            //чистим имя пользователя
            var nick = line.Nick;

            //обновляем базу пользователей
            if (_usersList.ContainsKey(nick))
            {
                _usersList[nick]._moments.Add(line.Time);
                _usersList[nick].StringFatness.Add(line.LineLength);
            }

            else
            {
                var moment = new List<DateTime> {line.Time};
                var fatness = new List<ushort> {line.LineLength};
                var newNick = new List<string> {nick};

                _usersList.Add(nick, new UserElement(newNick, moment, fatness));
            }
        }

        private void StopAllButton_Click(object sender, RoutedEventArgs e)
        {
            _task.FlushAll();
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {

            _history.Length = 0;
            _history.Capacity = 0;
            _history.Capacity = 16;
            Log.Text = "";
        }

        /// <summary> статистический обработчик  </summary>
        private void StatButton_Click(object sender, RoutedEventArgs e)
        {


            //заполняем словарь активных юзеров
            _usersList = new SortedDictionary<string, UserElement>();

            _task.Add(delegate
                {
                    //обрабатываем строки заполняя базу
                    foreach (var line in _linesList)
                    {
                        UpdateStatistic(line);
                    }
                });


            //убираем из словаря входов сортированного по именам имена пассивных пользователей
            _task.Add(delegate
                {
                    var templist = new SortedDictionary<string, string>();

                    foreach (var nick in _usersList)
                    {
                        if (_renamelist.ContainsKey(nick.Key)) templist.Add(nick.Key, _renamelist[nick.Key]);
                    }
                     
                    _renamelist = new SortedDictionary<string, string>(templist);
                });

            //убираем из словаря переименований имена пассивных пользователей
            _task.Add(delegate
            {
                var templist = new SortedDictionary<string, SortedDictionary<string, uint>>();

                foreach (var nick in _usersList)
                {
                    if (_joinNickslist.ContainsKey(nick.Key)) templist.Add(nick.Key, _joinNickslist[nick.Key]);
                }

                _joinNickslist = new SortedDictionary<string, SortedDictionary<string, uint>>(templist);
            });




            //создаём базу синонимов по базе входов/выходов
            SynonimBase = new SortedDictionary<string, SortedDictionary<string, uint>>();

            _task.Add(delegate
                {
                    //перебираем все элементы словаря входов сортированного по никам
                    foreach (var user in _joinNickslist)
                    {
                        //если в базе синонимов такого имени нет добавляем в неё такой ник
                        if (!SynonimBase.ContainsKey(user.Key)) SynonimBase.Add(user.Key, new SortedDictionary<string, uint>());
                        
                        //перебираем все хосты приписанные к этому нику
                        foreach (var hostelement in user.Value)
                        {
                            //перебираем все элементы словаря входов сортированном по хосту
                            foreach (var nick in _joinHostslist[hostelement.Key])
                            {
                                //если текущий ник из словаря входов сортированных по никам уже содержится в базе синонимов, то обновляем его количество
                                if (SynonimBase[user.Key].ContainsKey(nick.Key)) SynonimBase[user.Key][nick.Key]++;

                                //если нет - добавляем ник в базу синонимов
                                else SynonimBase[user.Key].Add(nick.Key,1);
                            }
                        }
                    }
                });




            //очищаем пользователей не имеющих синонимов
            _task.Add(delegate
                {
                    var synonimBaseUpdated = new SortedDictionary<string, SortedDictionary<string, uint>>();
                    foreach (var mainnick in SynonimBase)
                    {
                        if (mainnick.Value.Count <= 1) continue;

                        foreach (var nick in mainnick.Value)
                        {
                            if (!synonimBaseUpdated.ContainsKey(mainnick.Key)) synonimBaseUpdated.Add(mainnick.Key, new SortedDictionary<string, uint>());

                            if (!synonimBaseUpdated[mainnick.Key].ContainsKey(nick.Key)) synonimBaseUpdated[mainnick.Key].Add(nick.Key, nick.Value);
                        }
                    }

                    SynonimBase = new SortedDictionary<string, SortedDictionary<string, uint>>(synonimBaseUpdated);
                });




            //ищем пересечения между группами пользователей
            _task.Add(delegate
                {
                    var todeleteList = new List<string>();
                    var tempdict = new SortedDictionary<string, SortedDictionary<string, uint>>();

                    foreach (var syn in SynonimBase)
                    {
                        if (!tempdict.ContainsKey(syn.Key)) tempdict.Add(syn.Key,syn.Value);
                        foreach (var nick in syn.Value)
                        {
                            if (syn.Key == nick.Key || !SynonimBase.ContainsKey(nick.Key) || todeleteList.Contains(nick.Key)) continue;

                            SynonimBase[syn.Key] = new SortedDictionary<string, uint>(MergeDictionary(syn.Value, SynonimBase[nick.Key]));
                            todeleteList.Add(nick.Key);
                        }
                    }

                    foreach (var nick in todeleteList) if (SynonimBase.ContainsKey(nick)) SynonimBase.Remove(nick);

                });









            //заказываем отображения размера базы пользователей
            _task.Add(null, delegate
                {
                    {
                        _history.AppendLine(_usersList.Count.ToString());
                        UpdateLog("");
                    }
                });

           
        }
    }
}
