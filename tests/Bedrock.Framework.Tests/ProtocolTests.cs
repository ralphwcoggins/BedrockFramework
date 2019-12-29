using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Bedrock.Framework.Protocols;
using Microsoft.AspNetCore.Connections;
using Xunit;

namespace Bedrock.Framework.Tests
{
    public class ProtocolTests
    {
        [Fact]
        public async Task ReadMessagesWorks()
        {
            var options = new PipeOptions(useSynchronizationContext: false);
            var pair = DuplexPipe.CreateConnectionPair(options, options);
            await using var connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pair.Transport, pair.Application);
            var data = Encoding.UTF8.GetBytes("Hello World");
            for (int i = 0; i < 3; i++)
            {
                await connection.Application.Output.WriteAsync(data);
            }
            connection.Application.Output.Complete();

            var protocol = new TestProtocol(data.Length);
            var reader = connection.CreateReader();
            var count = 0;

            while (true)
            {
                var result = await reader.ReadAsync(protocol);

                if (result.IsCompleted)
                {
                    break;
                }

                count++;
                Assert.Equal(data, result.Message);

                reader.Advance();
            }

            Assert.Equal(3, count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(20)]
        public async Task PartialMessageWorks(int? maxMessageSize)
        {
            var options = new PipeOptions(useSynchronizationContext: false);
            var pair = DuplexPipe.CreateConnectionPair(options, options);
            await using var connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pair.Transport, pair.Application);
            var data = Encoding.UTF8.GetBytes("Hello World");
            var protocol = new TestProtocol(data.Length);
            var reader = connection.CreateReader(maxMessageSize);
            var resultTask = reader.ReadAsync(protocol);

            // Write byte by byte
            for (int i = 0; i < data.Length; i++)
            {
                await connection.Application.Output.WriteAsync(data.AsMemory(i, 1));
            }

            var result = await resultTask;
            Assert.Equal(data, result.Message);
            reader.Advance();
        }

        [Fact]
        public async Task ReadingWithoutCallingAdvanceThrows()
        {
            var options = new PipeOptions(useSynchronizationContext: false);
            var pair = DuplexPipe.CreateConnectionPair(options, options);
            await using var connection = new DefaultConnectionContext(Guid.NewGuid().ToString(), pair.Transport, pair.Application);
            var data = Encoding.UTF8.GetBytes("Hello World");
            var protocol = new TestProtocol(data.Length);
            var reader = connection.CreateReader();

            await connection.Application.Output.WriteAsync(data);
            var result = await reader.ReadAsync(protocol);
            Assert.Equal(data, result.Message);

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync(protocol));
        }

        public class TestProtocol : IMessageReader<byte[]>, IMessageWriter<byte[]>
        {
            public TestProtocol(int messageLength)
            {
                MessageLength = messageLength;
            }

            public int MessageLength { get; }

            public bool TryParseMessage(in ReadOnlySequence<byte> input, out SequencePosition consumed, out SequencePosition examined, out byte[] message)
            {
                consumed = input.Start;
                examined = input.End;

                if (input.Length < MessageLength)
                {
                    message = default;
                    return false;
                }

                var buffer = input.Slice(0, MessageLength);
                message = buffer.ToArray();
                consumed = buffer.End;
                examined = buffer.End;

                return true;
            }

            public void WriteMessage(byte[] message, IBufferWriter<byte> output)
            {
                output.Write(message);
            }
        }
    }
}