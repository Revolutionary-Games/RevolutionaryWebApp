# frozen_string_literal: true

require 'grape-swagger'

module API
  module V1
    class Base < Grape::API
      mount API::V1::Symbols
      mount API::V1::Stackwalk
      mount API::V1::CrashReport
      mount API::V1::LFS
      mount API::V1::PatreonWebhook
      mount API::V1::LFSFile
      mount API::V1::Launcher
      mount API::V1::Download

      add_swagger_documentation(
        api_version: 'v1',
        hide_documentation_path: true,
        mount_path: '/api/v1/swagger_doc',
        hide_format: true
      )
    end
  end
end
