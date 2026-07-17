cmake_minimum_required(VERSION 3.16)
project(game_server_cpp CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(game_server_cpp
    src/main.cpp
    src/NetworkServer.cpp
)

target_include_directories(game_server_cpp PRIVATE src include)

# Phase 4 SAST gate: warnings are errors, GCC analyzer always on.
target_compile_options(game_server_cpp PRIVATE
    -Wall -Wextra -Wpedantic -Werror
    $<$<CXX_COMPILER_ID:GNU>:-fanalyzer>
)
