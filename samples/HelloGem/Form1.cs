using System;
using System.Windows.Forms;
using IronGEM.Connector;

namespace HelloGem
{
    public partial class Form1 : Form
    {
        // IronGEM과 이야기하는 창구 하나
        private readonly IronGEMProxy proxy = new IronGEMProxy();

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // ① 이벤트를 먼저 등록합니다 — 이벤트는 Initialize를 부른 스레드(화면 스레드)로 들어옵니다.
            proxy.GEMCommStateChanged += state => label1.Text = "호스트 통신: " + (CommunicationState)state;

            // ② IronGEM에 연결합니다(같은 PC의 IronGEM, gRPC 포트 5001). 0이면 성공.
            int result = proxy.Initialize("127.0.0.1", 5001);
            Text = "HelloGem — Initialize 결과 " + result;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // ③ 값을 올립니다: SV 1001 ChamberTemp = 123.4 (IronStudio에 만든 VID)
            int result = proxy.SetVariable(1001, "123.4");
            button1.Text = "SetVariable 결과 " + result;
        }
    }
}
