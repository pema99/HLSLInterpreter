// This is an include file that can be used to provide utilities when writing HLSL unit tests.

#ifndef __HLSL_TEST_INCLUDED__
#define __HLSL_TEST_INCLUDED__

    // These should do nothing when not running tests.
    #ifndef __HLSL_TEST_RUNNER__
        #define printf
        #define PRINTF
        #define ASSERT(x)
        #define ASSERT_MSG(x, msg)
        #define ASSERT_EQUAL(a, b)
        #define ASSERT_NEAR(a, b, eps)
        #define ASSERT_UNIFORM(x)
        #define ASSERT_VARYING(x)
        #define PASS_TEST()
        #define PASS_TEST_MSG(msg)
        #define FAIL_TEST()
        #define FAIL_TEST_MSG(msg)
        #define IGNORE_TEST()
        #define IGNORE_TEST_MSG(msg)
        #define MOCK_RESOURCE(res, mock)
        #define TEST_NAME()
        #define TEST_CASE
        #define TEST_VALUE
    #endif

#endif