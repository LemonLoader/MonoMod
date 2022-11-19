;:; nasm -g -f elf64 -Ox exhelper_linux_x86_64.asm -o exhelper_linux_x86_64.o && ld -shared --eh-frame-hdr -z now -o exhelper_linux_x86_64.so exhelper_linux_x86_64.o

BITS 64
DEFAULT REL

%include "dwarf_eh.inc"
%include "macros.inc"

section .tbss ; TLS section
global cur_ex_ptr
cur_ex_ptr: resq 1

section .data

LSDA_mton:
    dd eh_managed_to_native.landingpad - $
LSDA_none:
    dd 0

section .text

CFI_INIT _personality

GLOBAL eh_has_exception:function
eh_has_exception:
    CFI_STARTPROC LSDA_none
    mov rax, [rel cur_ex_ptr wrt ..gottpoff]
    mov r10, [fs:rax]
    xor eax, eax
    test r10, r10
    setnz al
    ret
    CFI_ENDPROC

GLOBAL eh_managed_to_native:function
eh_managed_to_native:
    CFI_STARTPROC LSDA_mton
    FRAME_PROLOG

    ; note that for SetGR to behave, we need to have a stack slot for the reg we set
    ; reserve 2 stack slots, and make sure CFI knows that's where to put the info
    sub rsp, 8
    ; we'll actually save rax, so we can recover that if needed
    mov [rbp - 3*8], rax
    CFI_offset DW_REG_rax, -3*8 ; TODO: helper macros for register saving + CFI
    ; then we'll also shadow-save r10, but actually write a zero
    mov qword [rbp - 4*8], 0
    CFI_offset DW_REG_r+15, -4*8 ; we have to use r15, because that's what libunwind gives us for this purpose

    ; managed->native sets up an exception handler to catch unmanaged exceptions for an arbitrary entrypoint
    ; that entrypoint will be passed in rax, using a dynamically generated stub
    call rax ; we have to call because we have a stack frame for EH
    ; then just clean up our frame and return

    CFI_push

    FRAME_EPILOG
    ret

    CFI_pop

.landingpad:
    ; make sure to load r15 out of the stack frame
    mov r15, [rbp - 4*8]
    mov rax, [rel cur_ex_ptr wrt ..gottpoff]
    mov [fs:rax], r15
    
    ; clear rax for safety
    xor eax, eax
    FRAME_EPILOG
    ret
    
    CFI_ENDPROC
    

GLOBAL eh_native_to_managed:function
eh_native_to_managed:
    CFI_STARTPROC LSDA_none
    FRAME_PROLOG

    ; zero cur_ex_ptr
    mov r11, 0
    mov r10, [rel cur_ex_ptr wrt ..gottpoff]
    mov [fs:r10], r11

    ; native->managed calls into managed, then checks if an exception was caught by this helper on the other side, and rethrows if so
    call rax
    ; return value in rax now

    ; load cur_ex_ptr
    mov r10, [rel cur_ex_ptr wrt ..gottpoff]
    mov r10, [fs:r10]
    ; if it's nonzero, rethrow
    test r10, r10
    jnz .do_rethrow

    CFI_push

    ; otherwise, exit normally
    FRAME_EPILOG
    ret
    
    CFI_pop

.do_rethrow:
    mov rdi, r10
    call _Unwind_RaiseException wrt ..plt
    int3 ; deliberately don't handle failures at this point. This will have been a crash anyway.
    CFI_ENDPROC
    
; TODO: for some reason, when our personality is called in phase 2, it doesn't point to the same exception object it does in phase 1
; the pointer seems to be outright invalid in phase 2, while correct in phase 1

; argument passing:
; rdi, rsi, rdx, rcx, r8, r9
; int version, _Unwind_Actions actions, uint64 exceptionClass, _Unwind_Exception* exceptionObject, _Unwind_Context* context
_personality:
%push
    FRAME_PROLOG
    sub rsp, 6 * 8

    %define version rdi
    %define actions rsi
    %define exceptionClass rdx
    %define exceptionObject rcx
    %define context r8

    %define Sversion qword [rsp + 5*8]
    %define Sactions qword [rsp + 4*8]
    %define SexceptionClass qword [rsp + 3*8]
    %define SexceptionObject qword [rsp + 2*8]
    %define Scontext qword [rsp + 1*8]
    %define Srbx qword [rsp + 0*8]

    mov Sversion, version
    mov Sactions, actions
    mov SexceptionClass, exceptionClass
    mov SexceptionObject, exceptionObject
    mov Scontext, context
    mov Srbx, rbx

    ; rdi = version = 1

    test actions, _UA_FORCE_UNWIND
    jz .should_process
    mov rax, _URC_CONTINUE_UNWIND
    jmp .ret

.should_process:
    ; load the LSDA value into rbx, because we'll always need it after this point
    mov rdi, context
    call _Unwind_GetLanguageSpecificData wrt ..plt
    movsxd rbx, dword [rax]

    test Sactions, _UA_SEARCH_PHASE
    jz .handler_phase
    ; this is the search phase, do we have a handler?

    ; we want to check that the LSDA's pointer is non-null
    test ebx, ebx
    jz .no_handler

    mov rax, _URC_HANDLER_FOUND ; yes, we have a handler
    jmp .ret
.no_handler:
    mov rax, _URC_CONTINUE_UNWIND ; no, we don't have a handler
    jmp .ret

.handler_phase:
    ; check that Sactions contains _UA_HANDLER_FRAME
    test Sactions, _UA_HANDLER_FRAME
    jz .no_handler

    ; rax contains pLSDA, and rbx contains LSDA
    ; their sum is the landingpad
    add rax, rbx

    ; set our IP
    mov rdi, Scontext
    mov rsi, rax
    call _Unwind_SetIP WRT ..plt

    ; set r15 to contain our exception pointer
    mov rdi, Scontext
    mov rsi, DW_REG_r+15
    mov rdx, SexceptionObject
    call _Unwind_SetGR WRT ..plt

    mov rax, _URC_INSTALL_CONTEXT
    ;jmp .ret

.ret:
    mov rbx, Srbx
    FRAME_EPILOG
    ret
%pop

CFI_UNINIT
