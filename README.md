# RabbitMQ Messaging Service

This repository contains a .NET 5 Worker Service project that demonstrates message-based communication using RabbitMQ. The solution includes a broker service and test clients for sending and receiving messages.

## Features

- .NET 5 Worker Service architecture
- RabbitMQ integration for message brokering
- Example request/response message handlers
- Docker support for easy deployment

## Projects

- **RabbitMq.Broker**: Core broker service handling message routing and processing.
- **BrokerTester**: Sample client for sending and receiving messages.
- **BrokerTester1**: Additional test client for multi-client scenarios.

## Getting Started

### Prerequisites

- [.NET 5 SDK](https://dotnet.microsoft.com/download/dotnet/5.0)
- [RabbitMQ Server](https://www.rabbitmq.com/download.html)
- [Docker](https://www.docker.com/) (optional, for containerized deployment)

### Configuration

Update the `appsettings.json` files in each project to configure RabbitMQ connection settings:

### Running the Services

#### Using .NET CLI

1. Start RabbitMQ server locally or via Docker.
2. Build and run the broker service:
3. In separate terminals, run the test clients:

#### Using Docker

Each project includes a `Dockerfile`. Build and run containers as needed:

Repeat for `BrokerTester` and `BrokerTester1`.

## Usage

- The broker service listens for messages and routes them to appropriate handlers.
- Test clients send sample requests (e.g., `GetNameRequest`) and receive responses (`GetNameResponse`).

## Extending

- Add new message types by creating request/response classes and handlers.
- Register new handlers in the broker service.

## License

This project is licensed under the MIT License.

---

**Repository:** [RabbitMQ.MessagingService](https://github.com/Selvamadddy/RabbitMQ.MessagingService)