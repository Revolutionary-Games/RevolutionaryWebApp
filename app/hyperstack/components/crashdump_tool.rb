# frozen_string_literal: true

# Page for a form that allows manual crashdump processing
class CrashDumpTool < HyperComponent
  render(DIV) do
    H1 { 'Decode a crashdump created by Thrive' }
    BR {}
    H2 {
      'The Thrive Launcher provides a tool for creating a full crash report. Use that ' \
      'instead. Unless you specifically want to see what is inside a crashdump file'
    }
    HR {}

    P {
      "If Thrive has crashed it will generate a '.dmp' file." \
        'The file will be named a bunch of hexadecimal characters and it will be in the ' \
        "'Thrive/bin' (the same folder as the main executable) folder. An example name is " \
        "'e709d739-416a-43ee-7b902580-9f81d05a.dmp'"
    }

    P {
      'You can upload a crash dump (see above) with the form below. ' \
        'To get it in processed form.'
    }

    P {
      SPAN {
        'This tool does not store the dumps. So please copy the report ' \
          'and put it on '
      }
      A(href: 'https://pastebin.com') { 'pastebin' }
      SPAN {
        ' (or another text pasting service)'
      }
    }

    ReactStrap.Form(method: 'post', action: '/api/v1/stackwalk',
                    enctype: 'multipart/form-data') {
      ReactStrap.FormGroup {
        ReactStrap.Label(for: 'uploadFileSelect') { 'Please select the crash dump file: ' }
        ReactStrap.Input(type: :file, name: 'data')
      }
      ReactStrap.Button { 'Submit' }
    }
  end
end
