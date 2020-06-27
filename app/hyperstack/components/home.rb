# frozen_string_literal: true

# Main page content component
class Home < HyperComponent
  include Hyperstack::Router

  render(DIV) do
    H1 { 'Welcome to Thrive developer center' }

    P {
      SPAN {
        'This is a place where a bunch of different tools and services for use in ' \
             'Thrive developed are collected. If you want to report a crash please use the ' \
             'reporter in the thrive launcher. If you want to report some other kind of ' \
             'issue, please head on over to our '
      }
      A(href: 'https://community.revolutionarygamesstudio.com/') { 'community forums.' }
    }

    P {
      'If you are a Thrive developer log in using your Dev Forum account to see more options.'
    }

    P {
      'If you are a patron log in using your Patreon or community forum account to access ' \
      'devbuilds.'
    }

    P {
      'The manual crash dump file decoding option is now under "Tools"'
    }
  end
end
