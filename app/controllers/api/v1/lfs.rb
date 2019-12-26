# frozen_string_literal: true

require 'aws-sdk-s3'

module API
  module V1
    # LFS API endpoint
    class LFS < Grape::API
      include API::V1::Defaults

      content_type :json, 'application/vnd.git-lfs+json'

      DOWNLOAD_EXPIRE_TIME = 900
      UPLOAD_EXPIRE_TIME = 1800

      helpers do
        def request_auth
          error!('Unauthorized', 401,
                 'LFS-Authenticate' => 'Basic realm="ThriveDevCenter Git LFS"')
        end

        def check_server_config
          if !ENV['LFS_STORAGE_DOWNLOAD'] || !ENV['LFS_STORAGE_DOWNLOAD_KEY'] ||
             !ENV['LFS_STORAGE_S3_ENDPOINT'] || !ENV['LFS_STORAGE_S3_BUCKET'] ||
             !ENV['LFS_STORAGE_S3_REGION'] || !ENV['LFS_STORAGE_S3_ACCESS_KEY'] ||
             !ENV['LFS_STORAGE_S3_SECRET_KEY']
            error!('Invalid Server Configuration', 500)
          end
        end

        def check_auth
          if headers['Authorization']

            decoded = Base64.decode64(headers['Authorization'].split(' ', 2).second || '')

            username, token = decoded.split(':', 2)

            user = User.find_by email: username, lfs_token: token

            request_auth unless user

            return user
          end
          nil
        end

        def read_operation
          error!({ error_code: 400, message: 'Missing operation' }, 400) unless
            params[:operation]

          case params[:operation]
          when 'download'
            return :download
          when 'upload'
            return :upload
          else
            error!({ error_code: 400, message: 'Invalid operation' }, 400)
          end
        end

        def read_adapter
          if params[:transfers]
            if !params[:transfers].is_a?(Array) || params[:transfers].include?('basic')
              error!({ error_code: 400, message:
                                          'Only basic transfer adapter is supported' }, 400)
            end
          end

          :basic
        end

        def check_common_lfs_options
          if !params[:objects] || !params[:objects].is_a?(Array)
            error!({ error_code: 400, message: 'Missing objects array' }, 400)
          end

          [read_operation, read_adapter]
        end

        def handle_download(project, obj)
          object = LfsObject.find_by lfs_project_id: project.id, oid: obj[:oid]

          unless object
            return [{}, {
              code: 404,
              message: 'OID not found'
            }]
          end

          path = '/' + object.storage_path
          expires_at = Time.now.to_i + DOWNLOAD_EXPIRE_TIME

          unhashed_key = ENV['LFS_STORAGE_DOWNLOAD_KEY'] + path + expires_at.to_s

          # IP validation would be added here. unhashed_key += remote ip

          token = Base64.encode64(Digest::MD5.digest(unhashed_key))

          token = token.tr("\n", '').tr('+', '-').tr('/', '_').delete('=')

          url = URI.join(ENV['LFS_STORAGE_DOWNLOAD'],
                         path).to_s + "?token=#{token}&expires=#{expires_at}"

          [
            {
              download:
                {
                  "href": url,
                  expires_in: DOWNLOAD_EXPIRE_TIME
                }
            }, nil
          ]
        end

        def handle_upload(project, obj)
          # TODO: allow removing failed uploads
          object = LfsObject.find_by lfs_project_id: project.id, oid: obj[:oid]

          if object
            # We already have this object
            return [{}, nil]
          end

          aws_client = Aws::S3::Client.new(
            region: ENV['LFS_STORAGE_S3_REGION'],
            endpoint: ENV['LFS_STORAGE_S3_ENDPOINT'],
            access_key_id: ENV['LFS_STORAGE_S3_ACCESS_KEY'],
            secret_access_key: ENV['LFS_STORAGE_S3_SECRET_KEY']
          )

          oid = obj[:oid]

          storage_path = "#{project.slug}/objs/#{oid[0..1]}/#{oid[2..3]}/#{oid[4..]}"

          begin
            s3 = Aws::S3::Resource.new(client: aws_client)

            bucket = s3.bucket ENV['LFS_STORAGE_S3_BUCKET']

            # One extra call to AWS to make sure the target bucket is
            # valid to avoid configuration issues. Can be removed if
            # this is a performance issue
            unless bucket.exists?
              raise 'Target S3 bucket does not exist or configured credentials are wrong'
            end

            s3_obj = bucket.object(storage_path)

            # This probably doesn't connect to amazon at all, so we don't know if our
            # token is invalid here. So this step likely cannot fail
            url = s3_obj.presigned_url(:put, expires_in: UPLOAD_EXPIRE_TIME)

            lfs_object = LfsObject.create oid: oid, storage_path: storage_path,
                                          lfs_project: project, size: obj[:size]

            unless lfs_object.valid?
              return [{}, {
                code: 422,
                message: 'Object cannot be saved. Validation failures: ' +
                         lfs_object.errors.full_messages.join('.\n')
              }]
            end

            unless lfs_object.save
              return [{}, {
                code: 500,
                message: 'Object cannot be saved. Database write failed.'
              }]
            end

            [
              {
                upload:
                  {
                    "href": url,
                    expires_in: UPLOAD_EXPIRE_TIME
                  }
              }, nil
            ]
          rescue RuntimeError => e
            return [{}, {
              code: 500,
              message: "Internal server error: #{e}"
            }]
          end
        end

        def build_result_obj(obj, error, actions)
          result = {
            oid: obj[:oid],
            size: obj[:size],
            authenticated: true
          }

          if error
            result[:error] = error
          elsif !actions.empty?
            raise 'Actions must be a hash' unless actions.is_a?(Hash)

            result[:actions] = actions
          end

          result
        end

        def handle_lfs_object_request(project, obj, operation)
          error = nil
          actions = {}

          if operation == :download

            actions, error = handle_download project, obj

          elsif operation == :upload

            actions, error = handle_upload project, obj
          else
            error = {
              code: 500,
              message: 'Server operation logic error'
            }
          end

          build_result_obj obj, error, actions
        end
      end

      resource :lfs do
        desc 'Git LFS endpoints'
        get '' do
          { error: 'no project specified' }
        end

        params do
          optional :lfs_token, type: String
          requires :slug, type: String, desc: 'LFS project slug'
        end
        get ':slug' do
          user = nil

          if permitted_params[:lfs_token]
            user = User.find_by lfs_token: permitted_params[:lfs_token]
            error!({ error_code: 403, error: 'Invalid LFS token' }, 403) unless user
          end

          project = LfsProject.find_by slug: permitted_params[:slug]

          if !project || (!project.public? && !user&.developer?)

            error!({ error_code: 404, error: 'No project found' }, 404)
            return
          end

          { id: project.id, slug: project.slug, name: project.name }
        end

        params do
          requires :slug, type: String, desc: 'LFS project slug'
        end
        post ':slug/objects/batch' do
          user = check_auth

          content_type 'application/vnd.git-lfs+json'

          project = LfsProject.find_by slug: permitted_params[:slug]

          request_auth if !project&.public? && !user&.developer?

          error!({ error_code: 404, message: 'No project found' }, 404) unless project

          operation, adapter = check_common_lfs_options

          check_server_config

          if operation == :upload
            # Needs write access
            request_auth unless user&.developer?
          end

          processed_objects = []

          params[:objects].each { |obj|
            if !obj.include?(:oid) || !obj.include?(:size) || !obj[:size].is_a?(Integer) ||
               obj[:size].negative? || obj[:oid].length < 5
              processed_objects.push(
                Hash.new(
                  oid: obj[:oid],
                  size: obj[:size],
                  error: {
                    code: 422,
                    message: 'OID or size missing (or invalid)'
                  }
                )
              )
              next
            end

            processed_objects.push handle_lfs_object_request(project, obj, operation)
          }

          if processed_objects.empty?
            error!({ error_code: 422, message: 'No valid request object' }, 422)
          end

          status 200
          {
            transfer: adapter.to_s,
            objects: processed_objects
          }
        end

        post ':slug/locks/verify' do
          content_type 'application/vnd.git-lfs+json'
          status 501
          { message: 'Not implemented' }
        end
      end
    end
  end
end
