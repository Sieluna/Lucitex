cmake_minimum_required(VERSION 3.24)
project(LucitexFuzzNative LANGUAGES CXX)

file(GLOB_RECURSE ORACLE_PORT_FILES CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/ports/*")
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS ${ORACLE_PORT_FILES})

find_package(PNG REQUIRED)
find_package(JPEG REQUIRED)
find_package(WebP CONFIG REQUIRED)
find_package(OpenEXR 3.3 CONFIG REQUIRED)
find_package(Ktx CONFIG REQUIRED)

add_library(lucitex_fuzz_native SHARED
    OracleApi.cpp
    PngOracle.cpp
    ExrOracle.cpp
    Ktx2Oracle.cpp
    JpegOracle.cpp
    WebpOracle.cpp
)
target_compile_features(lucitex_fuzz_native PRIVATE cxx_std_17)
target_compile_definitions(lucitex_fuzz_native PRIVATE LUCITEX_ORACLE_BUILD)
target_link_libraries(lucitex_fuzz_native PRIVATE PNG::PNG JPEG::JPEG WebP::webp OpenEXR::OpenEXR KTX::ktx)
set_target_properties(lucitex_fuzz_native PROPERTIES
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
)

install(TARGETS lucitex_fuzz_native RUNTIME DESTINATION bin LIBRARY DESTINATION lib ARCHIVE DESTINATION lib)
install(FILES OracleApi.h DESTINATION include)
