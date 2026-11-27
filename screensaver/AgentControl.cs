using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace Znake
{
    [DataContract]
    internal sealed class AgentRequest
    {
        [DataMember] public string Command { get; set; }
        [DataMember] public string Token { get; set; }
        [DataMember] public string Direction { get; set; }
        [DataMember] public bool Bypass { get; set; }
    }

    [DataContract]
    internal sealed class AgentReply
    {
        [DataMember(EmitDefaultValue = false)] public string Error;
        [DataMember(EmitDefaultValue = false)] public string Token;
        [DataMember(EmitDefaultValue = false)] public string Status;
        [DataMember(EmitDefaultValue = false)] public bool Saved;
        [DataMember(EmitDefaultValue = false)] public AgentView View;
    }

    [DataContract]
    internal sealed class AgentView
    {
        [DataMember] public long Snake;
        [DataMember] public long Tick;
        [DataMember] public long Moves;
        [DataMember] public Cell Head;
        [DataMember] public int Heading;
        [DataMember] public double Energy;
        [DataMember] public double Stress;
        [DataMember] public double Capacity;
        [DataMember] public double VisionRadius;
        [DataMember] public Cell[] Fruit;
        [DataMember] public Rgb[] FruitColors;
        [DataMember] public AgentOption[] Options;
        [DataMember] public bool Paused;
    }

    [DataContract]
    internal sealed class AgentOption
    {
        [DataMember] public string Direction;
        [DataMember] public Cell Destination;
        [DataMember] public bool Legal;
        [DataMember] public bool Inside;
        [DataMember] public int JumpLayers;
        [DataMember] public double Cost;
        [DataMember] public double ColorStrength;
        [DataMember] public Rgb Color;
    }

    internal static class AgentControl
    {
        internal static AgentReply Apply(World world, double night, bool paused, string requestId,
            AgentRequest request, out bool changed)
        {
            changed = false;
            if (request == null || world == null || world.Field == null)
                return new AgentReply { Error = "The arena must be visible before an agent can take control." };
            string command = request.Command;
            string token = command == "spawn" ? requestId : request.Token;
            Snake snake = world.Snakes.Find(candidate => candidate.AgentToken != null && candidate.AgentToken == token);
            if (command == "spawn" && snake == null)
            {
                snake = world.Birth(null);
                snake.AgentToken = token;
                changed = true;
            }
            if (snake == null) return new AgentReply { Error = "No snake is assigned to this token." };
            var result = new AgentReply { Token = token, Status = "controlled" };

            if (command == "release")
            {
                snake.AgentToken = null;
                snake.LastAgentMove = null;
                changed = true;
                result.Status = "released";
            }
            else if (command == "move" && snake.LastAgentMove != requestId)
            {
                if (paused) return new AgentReply { Error = "Resume the world before sending a move." };
                int turn = request.Direction == "left" ? -1 : request.Direction == "right" ? 1 : 0;
                if (turn == 0 && request.Direction != "forward")
                    return new AgentReply { Error = "Choose forward, left, or right." };
                AgentOption action = Option(world, snake, turn, night);
                if (!action.Legal || (action.JumpLayers > 0 && !request.Bypass))
                    return new AgentReply { Error = "That move needs a legal destination and a bypass for occupied squares." };
                world.MoveAgent(snake, (snake.Heading + turn + 4) % 4, request.Bypass, night);
                snake.LastAgentMove = requestId;
                changed = true;
                if (!world.Snakes.Contains(snake))
                {
                    result.Status = "departed";
                    return result;
                }
            }
            else if (command != "spawn" && command != "sense" && command != "move")
                return new AgentReply { Error = "Choose spawn, sense, move, or release." };

            double radius = snake.EffectiveGene(Gene.VisionRadius, night);
            Fruit[] visible = world.Fruits.Where(fruit => radius > 0 &&
                (radius >= 100 || World.FoodDistance(snake.Body[0], fruit.Position) <= radius)).ToArray();
            result.View = new AgentView
            {
                Snake = snake.Id, Moves = snake.Moves, Tick = world.Ticks, Head = snake.Body[0],
                Heading = snake.Heading, Energy = snake.Energy, Stress = snake.Stress,
                Capacity = snake.Capacity(night, false), VisionRadius = radius, Paused = paused,
                Fruit = visible.Select(fruit => fruit.Position).ToArray(),
                FruitColors = visible.Select(fruit => fruit.Tone.SensitiveChannels(snake.Skin, snake.Sensitivity)).ToArray(),
                Options = new[] { Option(world, snake, -1, night), Option(world, snake, 0, night),
                    Option(world, snake, 1, night) }
            };
            return result;
        }

        private static AgentOption Option(World world, Snake snake, int turn, double night)
        {
            Cell head = snake.Body[0];
            Cell point = head.Move(Cell.Directions[(snake.Heading + turn + 4) % 4]);
            bool inside = world.Contains(point);
            int layers = inside ? world.JumpHeight(snake, point) : 0;
            return new AgentOption
            {
                Direction = turn < 0 ? "left" : turn > 0 ? "right" : "forward",
                Destination = point, Inside = inside,
                Legal = !snake.Entering || world.Contains(head) || (inside && turn == 0),
                JumpLayers = layers,
                Cost = (Math.Abs(turn) + 2 * layers) * snake.EffectiveGene(Gene.ManeuverCost, night),
                Color = inside ? world.Field.At(point).SensitiveChannels(snake.Skin, snake.Sensitivity) : new Rgb(),
                ColorStrength = inside ? world.SquareStrength(new[] { point }) : 0
            };
        }
    }

    internal sealed class AgentMailbox
    {
        private readonly string folder;
        public AgentMailbox(string profile) { folder = Path.Combine(profile, "agent-control"); }

        public void Poll(Func<string, AgentRequest, AgentReply> handle)
        {
            if (!Directory.Exists(folder)) return;
            foreach (string path in Directory.EnumerateFiles(folder, "*.request").Take(4))
            {
                string id = Path.GetFileNameWithoutExtension(path);
                Guid parsed;
                if (!Guid.TryParseExact(id, "N", out parsed)) continue;
                string reply = Path.Combine(folder, id + ".reply");
                if (!File.Exists(reply))
                {
                    AgentRequest request = JsonFiles.Read<AgentRequest>(path, false);
                    JsonFiles.Write(reply, null, handle(id, request), false);
                }
                File.Delete(path);
            }
        }
    }
}
