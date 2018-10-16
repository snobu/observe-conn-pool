run:
	dotnet run | ./chart.py

build:
	dotnet restore && dotnet build

clean:
	rm -rf obj bin
