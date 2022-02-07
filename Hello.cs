using Godot;
using System;
using System.Threading;
using Amqp;
using Amqp.Framing;
using Amqp.Types;

public class Hello : Sprite
{
  // Declare member variables here. Examples:
  // private int a = 2;
  // private string b = "text";

  //
  // Return message as string.
  //
  static String GetContent(Message msg)
  {
    object body = msg.Body;
    return body == null ? null : body.ToString();
  }
  // Called when the node enters the scene tree for the first time.
  public override void _Ready()
  {
    GD.Print("Hello from C# to Godot :)");

    String url = "amqp://10.88.0.10:5672";
    String commandInQueue = "COMMAND.IN";
    String gameEventOutQueue = "GAME.EVENT.OUT";
    //int loopcount = 1;

    //if (args.Length > 0)
    //    url = args[0];
    //if (args.Length > 1)
    //    loopcount = Convert.ToInt32(args[1]);

    Connection.DisableServerCertValidation = true;
    //Trace.TraceLevel = TraceLevel.Verbose;
    //Trace.TraceListener = (l, f, a) => Console.WriteLine(DateTime.Now.ToString("[hh:mm:ss.fff]") + " " + string.Format(f, a));


    // should use async non-blocking connection factory
    Address address = new Address(url);
    Connection connection = new Connection(address);
    Session session = new Session(connection);

    // topics are multicast
    // queues are anycast
    // https://stackoverflow.com/a/51595195

    Target gameEventOutTarget = new Target
      {
        Address = gameEventOutQueue,
        Capabilities = new Symbol[] { new Symbol("topic") }
      };
    SenderLink gameEventOutSender = new SenderLink(session, "srt-game-server-sender", gameEventOutTarget, null);

    Source commandInSource = new Source
    {
      Address = commandInQueue,
      Capabilities = new Symbol[] { new Symbol("queue") }
    };
    ReceiverLink gameEventOutReceiver = new ReceiverLink(session, "srt-game-server-receiver", commandInSource, null);

    //Message request = new Message("How do you do?");
    //gameEventOutSender.Send(request);

    //Message recvMsg = gameEventOutReceiver.Receive();
    //gameEventOutReceiver.Accept(recvMsg);

    //GD.Print(recvMsg.Body.ToString());

    //gameEventOutReceiver.Close();
    //gameEventOutSender.Close();
    //session.Close();
    //connection.Close();

    //Connection connection = null;

    //try
    //{
    //    Address address = new Address(url);
    //    connection = new Connection(address);
    //    Session session = new Session(connection);

    //    // Sender attaches to fixed request queue name
    //    SenderLink sender  = new SenderLink(session, "Interop.Client-sender", requestQueueName);

    //    // Receiver attaches to dynamic address.
    //    // Discover its name when it attaches.
    //    String replyTo = "";
    //    ManualResetEvent receiverAttached = new ManualResetEvent(false);
    //    OnAttached onReceiverAttached = (l, a) =>
    //    {
    //        replyTo = ((Source)a.Source).Address;
    //        receiverAttached.Set();
    //    };

    //    // Create receiver and wait for it to attach.
    //    ReceiverLink receiver = new ReceiverLink(
    //        session, "Interop.Client-receiver", new Source() { Dynamic = true }, onReceiverAttached);
    //    if (receiverAttached.WaitOne(10000))
    //    {
    //        // Receiver is attached.
    //        // Send a series of requests, gather and print responses.
    //        String[] requests = new String[] {
    //            "Twas brillig, and the slithy toves",
    //            "Did gire and gymble in the wabe.",
    //            "All mimsy were the borogoves,",
    //            "And the mome raths outgrabe."
    //        };

    //        for (int j = 0; j < loopcount; j++)
    //        {
    //            Console.WriteLine("Pass {0}", j);
    //            for (int i = 0; i < requests.Length; i++)
    //            {
    //                Message request = new Message(requests[i]);
    //                request.Properties = new Properties() { MessageId = "request" + i, ReplyTo = replyTo };
    //                sender.Send(request);
    //                Message response = receiver.Receive();
    //                if (null != response)
    //                {
    //                    receiver.Accept(response);
    //                    Console.WriteLine("Processed request: {0} -> {1}",
    //                        GetContent(request), GetContent(response));
    //                }
    //                else
    //                {
    //                    throw new ApplicationException(
    //                        String.Format("Receiver timeout receiving response {0}", i));
    //                }
    //            }
    //        }
    //    }
    //    else
    //    {
    //        throw new ApplicationException("Receiver attach timeout");
    //    }
    //    receiver.Close();
    //    sender.Close();
    //    session.Close();
    //    connection.Close();
    //}
    //catch (Exception e)
    //{
    //    Console.Error.WriteLine("Exception {0}.", e);
    //    if (null != connection)
    //    {
    //        connection.Close();
    //    }
    //}
  }

  //  // Called every frame. 'delta' is the elapsed time since the previous frame.
  //  public override void _Process(float delta)
  //  {
  //      
  //  }
}
