.PHONY: build test up down logs clean restart-a smoke

build:
	dotnet build

test:
	dotnet test

up:
	docker compose up --build

down:
	docker compose down -v

logs:
	docker compose logs -f api

restart-a:
	docker compose stop provider-a

smoke:
	@curl -s -X POST http://localhost:8080/api/v1/debitos \
		-H 'Content-Type: application/json' \
		-d '{"placa":"ABC1234"}' | python3 -m json.tool

clean:
	dotnet clean
	docker compose down -v --rmi local 2>/dev/null || true
